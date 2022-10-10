using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    [ExecuteAlways, RequireComponent(typeof(SkinnedMeshRenderer))]
    public class SkinDeformationRenderer : MeshInstanceBehaviour
    {
#if UNITY_EDITOR
        public static List<SkinDeformationRenderer> enabledInstances = new List<SkinDeformationRenderer>();
#endif
#if UNITY_2021_2_OR_NEWER
        public bool executeOnGPU = false;
#else
        private const bool executeOnGPU = false;
#endif
        [NonSerialized] public MeshBuffers meshAssetBuffers;

        [NonSerialized] public float[] fittedWeights = new float[0]; // used externally
        [NonSerialized] public bool fittedWeightsAvailable = false; // used externally

        [NonSerialized] private SkinnedMeshRenderer smr;

        [NonSerialized] private MaterialPropertyBlock smrProps;

        public event Action afterSkinDeformationCallback;

        private struct BlendInputShaderPropertyIDs
        {
            public int _FrameAlbedoLo;
            public int _FrameAlbedoHi;
            public int _FrameFraction;
            public int _ClipWeight;
        }

        private static readonly BlendInputShaderPropertyIDs[] BlendInputShaderProperties =
        {
            new BlendInputShaderPropertyIDs()
            {
                _FrameAlbedoLo = Shader.PropertyToID("_BlendInput0_FrameAlbedoLo"),
                _FrameAlbedoHi = Shader.PropertyToID("_BlendInput0_FrameAlbedoHi"),
                _FrameFraction = Shader.PropertyToID("_BlendInput0_FrameFraction"),
                _ClipWeight = Shader.PropertyToID("_BlendInput0_ClipWeight"),
            },
            new BlendInputShaderPropertyIDs()
            {
                _FrameAlbedoLo = Shader.PropertyToID("_BlendInput1_FrameAlbedoLo"),
                _FrameAlbedoHi = Shader.PropertyToID("_BlendInput1_FrameAlbedoHi"),
                _FrameFraction = Shader.PropertyToID("_BlendInput1_FrameFraction"),
                _ClipWeight = Shader.PropertyToID("_BlendInput1_ClipWeight"),
            },
        };

        [Serializable]
        private struct BlendInput
        {
            public bool active;
            public SkinDeformationClip clip;
            public float clipPosition;
            public float clipWeight;
        }

        private BlendInput[] blendInputs = new BlendInput[2];
        private BlendInput[] blendInputsPrev = new BlendInput[2];
        private bool blendInputsRendered = false;

        public bool UseCPUDeformation => !executeOnGPU;

        public Vector3[] CurrentDeformedPositions => blendedPositions;

        public Vector3[] CurrentDeformedNormals => blendedNormals;

        private Vector3[] blendedPositions;
        private Vector3[] blendedNormals;

        [Header("Stream options")] 
        public bool renderAlbedo;
        public bool renderFittedWeights;
        [Range(1.0f, 10.0f)] public float renderFittedWeightsScale = 1.0f;
        private bool renderFittedWeightsPrev;
        
        [Header("Blendshape overrides")] 
        public bool muteFacialRig = false;
        [TextArea(1, 20)] public string muteFacialRigExclude = "";
        private string muteFacialRigExcludePrev = null;
        private bool[] muteFacialRigExcludeMark = new bool[0];

       
        private bool resourcesCreatedForGPU;

#if UNITY_2021_2_OR_NEWER
        // GPU Deform resource
        private ComputeShader skinDeformCS;
        private int applyDeformKernel;
        private ComputeBuffer positionAndNormalDeltasBuffer;
        private ComputeBuffer neutralPositionsBuffer;
        private ComputeBuffer neutralNormalsBuffer;

        private NativeArray<half> positionAndNormalDeltaCPUBuffer;

        static class Uniforms
        {
            internal static int _NeutralPositions = Shader.PropertyToID("_NeutralPositions");
            internal static int _NeutralNormals = Shader.PropertyToID("_NeutralNormals");

            internal static int _TargetMeshPosNormalBuffer = Shader.PropertyToID("_TargetMeshPosNormalBuffer");

            internal static int _NumberOfVertices = Shader.PropertyToID("_NumberOfVertices");
            internal static int _PositionStrideOffset = Shader.PropertyToID("_PositionStrideOffset");
            internal static int _NormalStrideOffset = Shader.PropertyToID("_NormalStrideOffset");

            //apply
            internal static int _PositionAndNormalDeltas = Shader.PropertyToID("_PositionAndNormalDeltas");
        }
#endif
        
        protected override void OnMeshInstanceCreated()
        {
            blendInputsRendered = false;

            //Debug.Log("OnMeshInstanceCreated from meshAsset " + meshAsset.GetInstanceID());
            if (meshAssetBuffers == null)
                meshAssetBuffers = new MeshBuffers(meshAsset);
            else
                meshAssetBuffers.LoadFrom(meshAsset);

#if UNITY_2021_2_OR_NEWER
            if (!UseCPUDeformation)
            {
                //set required gpu buffer flags
                meshInstance.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                EnsureGPUDeformationResources();
            }
#endif
        }

        protected override void OnMeshInstanceDeleted()
        {
            
        }
        
        void OnEnable()
        {
            if (smr == null)
                smr = GetComponent<SkinnedMeshRenderer>();

            if (smrProps == null)
                smrProps = new MaterialPropertyBlock();
#if UNITY_EDITOR
            if (SkinDeformationRenderer.enabledInstances.Contains(this) == false)
                SkinDeformationRenderer.enabledInstances.Add(this);
#endif
            InitResources();
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            SkinDeformationRenderer.enabledInstances.Remove(this);
#endif

            if (smr == null || smr.sharedMesh == null || smr.sharedMesh.GetInstanceID() >= 0)
                return;

            for (int i = 0; i != smr.sharedMesh.blendShapeCount; i++)
                smr.SetBlendShapeWeight(i, 0.0f);

            FreeResources();
        }

        void InitResources()
        {
            EnsureMeshInstance();
            resourcesCreatedForGPU = !UseCPUDeformation;
        }
        
        void FreeResources()
        {
#if UNITY_2021_2_OR_NEWER
            if (resourcesCreatedForGPU)
            {
                DestroyGPUDeformationResources();
            }
#endif
            RemoveMeshInstance();
        }

        void OnDestroy()
        {
            RemoveMeshInstance();
        }

        
        public void SetBlendInput(int index, SkinDeformationClip clip, float clipPosition, float clipWeight)
        {
            Debug.Assert(index >= 0 && index < blendInputs.Length);
            Debug.Assert(clip == null || meshAssetBuffers.vertexCount == clip.frameVertexCount);

            blendInputs[index].active = (clip != null) && (clipWeight > 0.0f);
            blendInputs[index].clip = clip;
            blendInputs[index].clipPosition = clipPosition;
            blendInputs[index].clipWeight = clipWeight;

            //if (clip != null)
            //	Debug.Log("SetBlendInput[" + index + "] clip=" + clip.name + ", position=" + clipPosition + ", weight=" + clipWeight);
            //else
            //	Debug.Log("SetBlendInput[" + index + "] clip=NULL, position=" + clipPosition + ", weight=" + clipWeight);
        }

        void LateUpdate()
        {
            if ( (resourcesCreatedForGPU != !UseCPUDeformation))
            {
                FreeResources();
                InitResources();
            }

            var blendInputsChanged = false;
            {
                for (int i = 0; i != blendInputs.Length; i++)
                {
                    blendInputsChanged |= (blendInputs[i].active != blendInputsPrev[i].active);
                    blendInputsChanged |= (blendInputs[i].clip != blendInputsPrev[i].clip);
                    blendInputsChanged |= (blendInputs[i].clipPosition != blendInputsPrev[i].clipPosition);
                    blendInputsChanged |= (blendInputs[i].clipWeight != blendInputsPrev[i].clipWeight);
                }
            }

            if (blendInputsChanged)
                blendInputsRendered = false;

            if (blendInputsRendered && renderFittedWeights == renderFittedWeightsPrev && muteFacialRig == false)
                return;

            RenderBlendInputs();

            for (int i = 0; i != blendInputs.Length; i++)
                blendInputsPrev[i] = blendInputs[i];

            renderFittedWeightsPrev = renderFittedWeights;
#if UNITY_2021_2_OR_NEWER
            if (!UseCPUDeformation)
            {
                ExecuteSkinDeformationCompute();
            }
#endif
            afterSkinDeformationCallback?.Invoke();
        }


        
        
        public void RenderBlendInputs()
        {
            int fittedWeightsBufferSize = 0;
            {
                fittedWeightsAvailable = false;
                for (int i = 0; i != blendInputs.Length; i++)
                {
                    if (blendInputs[i].active == false || blendInputs[i].clip.framesContainFittedWeights == false)
                        continue;

                    fittedWeightsBufferSize =
                        Mathf.Max(fittedWeightsBufferSize, blendInputs[i].clip.frameFittedWeightsCount);
                    fittedWeightsAvailable = true;
                }

                if (fittedWeightsAvailable)
                    ArrayUtils.ResizeChecked(ref fittedWeights, fittedWeightsBufferSize);

                for (int i = 0; i != fittedWeights.Length; i++)
                    fittedWeights[i] = 0.0f;
            }

            ArrayUtils.ResizeChecked(ref blendedPositions, meshAssetBuffers.vertexCount);
            ArrayUtils.ResizeChecked(ref blendedNormals, meshAssetBuffers.vertexCount);

            if (UseCPUDeformation)
            {
                Array.Copy(meshAssetBuffers.vertexPositions, blendedPositions, meshAssetBuffers.vertexCount);
                Array.Copy(meshAssetBuffers.vertexNormals, blendedNormals, meshAssetBuffers.vertexCount);
            }
            else
            {
                Array.Clear(blendedPositions, 0, blendedPositions.Length);
                Array.Clear(blendedNormals, 0, blendedNormals.Length);
            }

            {
                if (smrProps == null)
                    smrProps = new MaterialPropertyBlock();

                smr.GetPropertyBlock(smrProps);
                {
                    for (int i = 0; i != blendInputs.Length; i++)
                        RenderBlendInputAdditive(i);
                }
                smr.SetPropertyBlock(smrProps);
            }

            if (UseCPUDeformation)
            {
                meshInstance.SilentlySetVertices(blendedPositions);
                meshInstance.SilentlySetNormals(blendedNormals);
            }

            if (renderFittedWeights)
            {
                if (fittedWeightsAvailable == false)
                    Debug.LogWarning(
                        "SkinDeformationRenderer is trying to render fitted weights, but none are available", this);

                for (int i = 0; i != fittedWeights.Length; i++)
                    smr.SetBlendShapeWeight(i, 100.0f * (fittedWeights[i] * renderFittedWeightsScale));
            }
            else
            {
                if (renderFittedWeightsPrev)
                {
                    for (int i = 0; i != fittedWeights.Length; i++)
                        smr.SetBlendShapeWeight(i, 0.0f);
                }

                if (muteFacialRig)
                {
                    var blendShapeCount = meshInstance.blendShapeCount;
                    if (blendShapeCount != muteFacialRigExcludeMark.Length ||
                        muteFacialRigExclude != muteFacialRigExcludePrev)
                    {
                        ArrayUtils.ResizeChecked(ref muteFacialRigExcludeMark, blendShapeCount);
                        Array.Clear(muteFacialRigExcludeMark, 0, blendShapeCount);

                        var excludeNames = muteFacialRigExclude.Split(',');
                        foreach (var excludeName in excludeNames)
                        {
                            var excludeIndex =
                                meshInstance.GetBlendShapeIndex(meshAsset.name + "_" + excludeName.Trim());
                            if (excludeIndex != -1)
                            {
                                muteFacialRigExcludeMark[excludeIndex] = true;
                            }
                        }

                        muteFacialRigExcludePrev = muteFacialRigExclude;
                    }

                    for (int i = 0; i != blendShapeCount; i++)
                    {
                        if (muteFacialRigExcludeMark[i] == false)
                            smr.SetBlendShapeWeight(i, 0.0f);
                    }
                }
                else
                {
                    ArrayUtils.ResizeChecked(ref muteFacialRigExcludeMark, 0);
                }
            }

            blendInputsRendered = true;
        }


        void RenderBlendInputAdditive(int index)
        {
            //Debug.Log("RenderBlendInputAdditive " + index + " (active " + blendInputs[index].active + ")");

            // early out for inactive input
            if (blendInputs[index].active == false)
            {
                smrProps.SetTexture(BlendInputShaderProperties[index]._FrameAlbedoLo, Texture2D.blackTexture);
                smrProps.SetTexture(BlendInputShaderProperties[index]._FrameAlbedoHi, Texture2D.blackTexture);
                smrProps.SetFloat(BlendInputShaderProperties[index]._ClipWeight, 0.0f);
                return;
            }

            // pos   = [0.0......1.0] =>
            // index = [0........N-1]
            var clip = blendInputs[index].clip;
            var clipPosition = Mathf.Clamp01(blendInputs[index].clipPosition);
            var clipWeight = Mathf.Clamp01(blendInputs[index].clipWeight);

            float subframeInterval = 1.0f / clip.subframeCount;
            float subframePosition = clipPosition / subframeInterval;
            float subframeFraction = subframePosition - Mathf.Floor(subframePosition);

            int subframeIndex = Mathf.Clamp((int) Mathf.Floor(subframePosition), 0, clip.subframeCount - 1);

            float frameFraction = Mathf.Lerp(clip.subframes[subframeIndex].fractionLo,
                clip.subframes[subframeIndex].fractionHi, subframeFraction);
            float frameWeightLo = Mathf.Max(0.0f, 1.0f - frameFraction);
            float frameWeightHi = Mathf.Min(1.0f, frameFraction);

            int frameIndexLo = clip.subframes[subframeIndex].frameIndexLo;
            int frameIndexHi = clip.subframes[subframeIndex].frameIndexHi;
            //Debug.Log("frameIndexLo " + frameIndexLo + ", frameIndexHi " + frameIndexHi + ", frameFraction " + frameFraction);

            unsafe
            {
                SkinDeformationClip.Frame frameLo = clip.GetFrame(frameIndexLo);
                SkinDeformationClip.Frame frameHi = clip.GetFrame(frameIndexHi);

                fixed (Vector3* outputPositions = blendedPositions)
                fixed (Vector3* outputNormals = blendedNormals)
                {
                    const int innerLoopBatchCount = 128; //TODO?

                    var jobPositions = new AddBlendedDeltaJob()
                    {
                        deltaA = (Vector3*) (frameLo.deltaPositions),
                        deltaB = (Vector3*) (frameHi.deltaPositions),
                        output = outputPositions,
                        cursor = frameFraction,
                        weight = clipWeight,
                    };
                    var jobNormals = new AddBlendedDeltaJob()
                    {
                        deltaA = (Vector3*) (frameLo.deltaNormals),
                        deltaB = (Vector3*) (frameHi.deltaNormals),
                        output = outputNormals,
                        cursor = frameFraction,
                        weight = clipWeight,
                    };

                    var jobHandlePositions = jobPositions.Schedule(clip.frameVertexCount, innerLoopBatchCount);
                    var jobHandleNormals = jobNormals.Schedule(clip.frameVertexCount, innerLoopBatchCount);

                    JobHandle.ScheduleBatchedJobs();

                    // do something useful before blocking on complete
                    {
                        if (clip.framesContainAlbedo && renderAlbedo)
                        {
                            smrProps.SetTexture(BlendInputShaderProperties[index]._FrameAlbedoLo, frameLo.albedo);
                            smrProps.SetTexture(BlendInputShaderProperties[index]._FrameAlbedoHi, frameHi.albedo);
                            smrProps.SetFloat(BlendInputShaderProperties[index]._FrameFraction, frameFraction);
                            smrProps.SetFloat(BlendInputShaderProperties[index]._ClipWeight, clipWeight);
                        }
                        else
                        {
                            smrProps.SetTexture(BlendInputShaderProperties[index]._FrameAlbedoLo,
                                Texture2D.blackTexture);
                            smrProps.SetTexture(BlendInputShaderProperties[index]._FrameAlbedoHi,
                                Texture2D.blackTexture);
                            smrProps.SetFloat(BlendInputShaderProperties[index]._ClipWeight, 0.0f);
                        }

                        if (clip.framesContainFittedWeights)
                        {
                            var fittedWeightsLo = frameLo.fittedWeights;
                            var fittedWeightsHi = frameHi.fittedWeights;

                            for (int i = 0; i != clip.frameFittedWeightsCount; i++)
                                fittedWeights[i] += clipWeight * Mathf.Lerp(fittedWeightsLo[i], fittedWeightsHi[i],
                                    frameFraction);
                        }
                    }

                    jobHandlePositions.Complete();
                    jobHandleNormals.Complete();
                }
            }
        }
        
        [BurstCompile(FloatMode = FloatMode.Fast)]
        unsafe struct AddBlendedDeltaJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction] public Vector3* deltaA;
            [NativeDisableUnsafePtrRestriction] public Vector3* deltaB;
            [NativeDisableUnsafePtrRestriction] public Vector3* output;

            public float cursor;
            public float weight;

            public void Execute(int i)
            {
                output[i] += weight * Vector3.Lerp(deltaA[i], deltaB[i], cursor);
            }
        }

#region GPUDeformation
#if UNITY_2021_2_OR_NEWER
    
        void PackDeltaPosAndNormal()
        {
            unsafe
            {
                fixed (Vector3* positions = blendedPositions)
                fixed (Vector3* normals = blendedNormals)

                {
                    var job = new PackPositionAndNormalDelta()
                    {
                        positions = positions,
                        normals = normals,
                        output = (half*)positionAndNormalDeltaCPUBuffer.GetUnsafePtr()
                    };
                    job.Schedule(blendedPositions.Length, 128).Complete();
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        unsafe struct PackPositionAndNormalDelta : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction] public Vector3* positions;
            [NativeDisableUnsafePtrRestriction] public Vector3* normals;
            [NativeDisableUnsafePtrRestriction] public half* output;
            
            public void Execute(int i)
            {
                float3 p = positions[i];
                float3 n = normals[i];

                half3 ph = math.half3(p);
                half3 nh = math.half3(n);

                output[i * 6] = ph.x;
                output[i * 6 + 1] = ph.y;
                output[i * 6 + 2] = ph.z;
                
                output[i * 6 + 3] = nh.x;
                output[i * 6 + 4] = nh.y;
                output[i * 6 + 5] = nh.z;

            }
        }

        void ExecuteSkinDeformationCompute()
        {
            const int groupSize = 64;

            PackDeltaPosAndNormal();

            positionAndNormalDeltasBuffer.SetData(positionAndNormalDeltaCPUBuffer);

            int posStream = meshInstance.GetVertexAttributeStream(VertexAttribute.Position);

            int[] posStrideOffset =
            {
                meshInstance.GetVertexBufferStride(posStream),
                meshInstance.GetVertexAttributeOffset(VertexAttribute.Position)
            };
            int[] normalStrideOffset =
            {
                meshInstance.GetVertexBufferStride(posStream),
                meshInstance.GetVertexAttributeOffset(VertexAttribute.Normal)
            };
            
            using GraphicsBuffer meshPosBuffer = meshInstance.GetVertexBuffer(posStream);

            CommandBuffer cmd = CommandBufferPool.Get("Skin Deformation");

            cmd.SetComputeIntParam(skinDeformCS, Uniforms._NumberOfVertices, meshAssetBuffers.vertexCount);

            cmd.SetComputeIntParams(skinDeformCS, Uniforms._PositionStrideOffset, posStrideOffset);
            cmd.SetComputeIntParams(skinDeformCS, Uniforms._NormalStrideOffset, normalStrideOffset);

            {
                cmd.BeginSample("Skin Deformation Apply");
                
                cmd.SetComputeBufferParam(skinDeformCS, applyDeformKernel, Uniforms._NeutralPositions,
                    neutralPositionsBuffer);
                cmd.SetComputeBufferParam(skinDeformCS, applyDeformKernel, Uniforms._NeutralNormals,
                    neutralNormalsBuffer);
                cmd.SetComputeBufferParam(skinDeformCS, applyDeformKernel, Uniforms._PositionAndNormalDeltas,
                    positionAndNormalDeltasBuffer);

                cmd.SetComputeBufferParam(skinDeformCS, applyDeformKernel, Uniforms._TargetMeshPosNormalBuffer,
                    meshPosBuffer);

                int dispatchCount = (meshAssetBuffers.vertexCount + groupSize - 1) / groupSize;
                cmd.DispatchCompute(skinDeformCS, applyDeformKernel, dispatchCount, 1, 1);

                cmd.EndSample("Skin Deformation Apply");
            }

            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        void EnsureGPUDeformationResources()
        {
            if (skinDeformCS == null || positionAndNormalDeltasBuffer == null)
            {
                CreateGPUDeformationResources();
            }
        }

        bool CreateGPUDeformationResources()
        {
            DestroyGPUDeformationResources();

            if (skinDeformCS == null)
            {
                skinDeformCS = Resources.Load<ComputeShader>("SkinDeformationCS");
            }

            if (skinDeformCS == null)
            {
                return false;
            }

            applyDeformKernel = skinDeformCS.FindKernel("ApplySkinDeform");

            const int packedPosNormEntrySizeBytes = 2 * 3 + 2 * 3;
            
            positionAndNormalDeltasBuffer = new ComputeBuffer(meshAssetBuffers.vertexCount, packedPosNormEntrySizeBytes, ComputeBufferType.Raw);
            positionAndNormalDeltaCPUBuffer = new NativeArray<half>(meshAssetBuffers.vertexCount * 6,
                Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            neutralPositionsBuffer = new ComputeBuffer(meshAssetBuffers.vertexCount, 4 * 3);
            neutralNormalsBuffer = new ComputeBuffer(meshAssetBuffers.vertexCount, 4 * 3);

            //upload static data
            neutralPositionsBuffer.SetData(meshAssetBuffers.vertexPositions);
            neutralNormalsBuffer.SetData(meshAssetBuffers.vertexNormals);

            return true;
        }

        void DestroyGPUDeformationResources()
        {
            if (positionAndNormalDeltasBuffer != null)
            {
                positionAndNormalDeltasBuffer.Dispose();
                neutralPositionsBuffer.Dispose();
                neutralNormalsBuffer.Dispose();
                positionAndNormalDeltaCPUBuffer.Dispose();
                
                positionAndNormalDeltasBuffer = null;
                neutralPositionsBuffer = null;
                neutralNormalsBuffer = null;
            }
        }

#endif
#endregion
    }
}