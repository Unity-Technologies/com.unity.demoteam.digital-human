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
        public bool forceCPUDeformation = false;
#else
        private const bool forceCPUDeformation = true;
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

        public bool UseCPUDeformation => forceCPUDeformation;

        public Vector3[] CurrentDeformedPositionOffsets => blendedPositions;

        public Vector3[] CurrentDeformedNormalOffsets => blendedNormals;

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

        [Header("Tension Sampler")] 
        public bool tensionSampleEnable = true;
        public float tensionGain = 1.0f;
        public Mesh tensionBasePose;

        // Tension Burst Jobs
        [NonSerialized] private MeshAdjacency tensionMeshAdjacency;

        private JobHandle tensionStagingJob;
        private JobHandle tensionsRestStageJob;
        private NativeArray<float> tensionEdgeRestLengths;
        private NativeArray<int> tensionAdjacentVertices;
        private NativeArray<int> tensionAdjacentCount;
        private NativeArray<int> tensionAdjacentOffset;
        private NativeArray<Color> tensionColors;
        // END Tension
        private bool resourcesCreatedForTensions;
        private bool resourcesCreatedForGPU;

        private Mesh lastTensionBasePose = null;

#if UNITY_2021_2_OR_NEWER
        // GPU Deform resource
        private ComputeShader skinDeformCS;
        private int applyDeformKernel;
        private int calculateSkinTensionKernel;
        private ComputeBuffer positionAndNormalDeltasBuffer;
        private ComputeBuffer neutralPositionsBuffer;
        private ComputeBuffer neutralNormalsBuffer;

        private ComputeBuffer edgeRestLengthsBuffer;
        private ComputeBuffer adjacentVerticesBuffer;
        private ComputeBuffer adjacentVerticesCountBuffer;
        private ComputeBuffer adjacentOffsetsBuffer;

        private NativeArray<half> positionAndNormalDeltaCPUBuffer;

        static class Uniforms
        {
            internal static int _NeutralPositions = Shader.PropertyToID("_NeutralPositions");
            internal static int _NeutralNormals = Shader.PropertyToID("_NeutralNormals");

            internal static int _TargetMeshPosNormalBuffer = Shader.PropertyToID("_TargetMeshPosNormalBuffer");
            internal static int _TargetMeshColorBuffer = Shader.PropertyToID("_TargetMeshColorBuffer");

            internal static int _NumberOfVertices = Shader.PropertyToID("_NumberOfVertices");
            internal static int _PositionStrideOffset = Shader.PropertyToID("_PositionStrideOffset");
            internal static int _NormalStrideOffset = Shader.PropertyToID("_NormalStrideOffset");
            internal static int _ColorStrideOffset = Shader.PropertyToID("_ColorStrideOffset");

            //apply
            internal static int _PositionAndNormalDeltas = Shader.PropertyToID("_PositionAndNormalDeltas");

            //tension
            internal static int _EdgeRestLengthsBuffer = Shader.PropertyToID("_EdgeRestLengthsBuffer");
            internal static int _AdjacentVerticesBuffer = Shader.PropertyToID("_AdjacentVerticesBuffer");
            internal static int _AdjacentVerticesCountBuffer = Shader.PropertyToID("_AdjacentVerticesCountBuffer");
            internal static int _AdjacentOffsetsBuffer = Shader.PropertyToID("_AdjacentOffsetsBuffer");
            internal static int _SkinTensionGain = Shader.PropertyToID("_SkinTensionGain");
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

            // TENSION SAMPLER
            if (tensionSampleEnable)
            {
                if (tensionMeshAdjacency == null)
                    tensionMeshAdjacency = new MeshAdjacency(meshAssetBuffers, true);
                else
                    tensionMeshAdjacency.LoadFrom(meshAssetBuffers);
                
                //force color attribute for the mesh
                if (meshInstance.colors.Length == 0) 
                {
                    Color32[] colorsTemp = new Color32[meshInstance.vertexCount];
                    meshInstance.SetColors(colorsTemp);

                }
            }

            // TENSION
            if (tensionSampleEnable)
            {
                BuildAdjVertexArrays();
                lastTensionBasePose = tensionBasePose;
            }
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
            resourcesCreatedForTensions = tensionSampleEnable;
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
            if (resourcesCreatedForTensions)
            {
                DestroyAdjVertexArrays();
            }
            
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
            if ( (resourcesCreatedForTensions != tensionSampleEnable) ||  (resourcesCreatedForGPU != !UseCPUDeformation) || lastTensionBasePose != tensionBasePose)
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
                
                if (tensionSampleEnable)
                {

                    UnityEngine.Profiling.Profiler.BeginSample("Tension: LateUpdate");
                    tensionStagingJob = CalculateEdgeLengthsJob(tensionsRestStageJob, blendedPositions);
                    tensionStagingJob.Complete();
                    meshInstance.SetColors(tensionColors);
                    UnityEngine.Profiling.Profiler.EndSample();
                }
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


        // TENSION SAMPLER - Burst jobs and methods --------------------------------------------------------------------

        void BuildAdjVertexArrays()
        {
            DestroyAdjVertexArrays();
            // Initialize
            tensionEdgeRestLengths = new NativeArray<float>(tensionMeshAdjacency.vertexCount, Allocator.Persistent);
            if (UseCPUDeformation)
            {
                tensionColors = new NativeArray<Color>(tensionMeshAdjacency.vertexCount, Allocator.Persistent);
                for (int i = 0; i < tensionMeshAdjacency.vertexCount; i++)
                {
                    tensionColors[i] = Color.black;
                }
            }
            
            // Temp vars
            List<int> fullVtxList = new List<int>();
            List<int> vtxOffset = new List<int>();
            List<int> edgesCount = new List<int>();

            // Build arrays
            int vtxOffsetCnt = 0;
            for (int i = 0; i < tensionMeshAdjacency.vertexCount; i++)
            {
                List<int> dataCheckVtx = new List<int>();
                int idx = tensionMeshAdjacency.vertexResolve[i];
                foreach (var j in tensionMeshAdjacency.vertexVertices[idx])
                {
                    dataCheckVtx.Add(j);
                    fullVtxList.Add(j);
                }

                edgesCount.Add(dataCheckVtx.Count);
                vtxOffsetCnt += dataCheckVtx.Count;
                vtxOffset.Add(vtxOffsetCnt - dataCheckVtx.Count);
            }

            // Convert to native arrays
            GetNativeIntArray(fullVtxList.ToArray(), ref tensionAdjacentVertices);
            GetNativeIntArray(vtxOffset.ToArray(), ref tensionAdjacentOffset);
            GetNativeIntArray(edgesCount.ToArray(), ref tensionAdjacentCount);

            // Calculate the rest edge lengths
            unsafe
            {
                tensionsRestStageJob = CalculateEdgeRestLengthsJob();
            }

            JobHandle.ScheduleBatchedJobs();
            tensionsRestStageJob.Complete();
        }

        void DestroyAdjVertexArrays()
        {
            if (!tensionEdgeRestLengths.IsCreated) 
                return;
            tensionEdgeRestLengths.Dispose();
            tensionAdjacentVertices.Dispose();
            tensionAdjacentCount.Dispose();
            tensionAdjacentOffset.Dispose();
            if (tensionColors.IsCreated)
            {
                tensionColors.Dispose();
            }
            
        }
 
        unsafe void GetNativeIntArray(int[] vertexArray, ref NativeArray<int> verts)
        {
            verts = new NativeArray<int>(vertexArray.Length, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            fixed (void* vertexBufferPointer = vertexArray)
            {
                UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(verts),
                    vertexBufferPointer, vertexArray.Length * (long) UnsafeUtility.SizeOf<int>());
            }
        }

        private unsafe JobHandle CalculateEdgeRestLengthsJob()
        {
            Vector3[] positions = tensionBasePose ? tensionBasePose.vertices : meshAssetBuffers.vertexPositions;

            fixed (Vector3* meshPositions = positions)
            {
                var job = new ResolveRestLengthJob()
                {
                    meshPositions = meshPositions,
                    restLengths = tensionEdgeRestLengths,
                    adjacentVertices = tensionAdjacentVertices,
                    adjacentCount = tensionAdjacentCount,
                    adjacentOffset = tensionAdjacentOffset,
                };
                return job.Schedule(tensionMeshAdjacency.vertexCount, 128);
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        unsafe struct ResolveRestLengthJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* meshPositions;

            [ReadOnly] public NativeArray<int> adjacentVertices;
            [ReadOnly] public NativeArray<int> adjacentCount;
            [ReadOnly] public NativeArray<int> adjacentOffset;
            
            [WriteOnly] public NativeArray<float> restLengths;

            public void Execute(int i)
            {
                var edgeLenght = 0.0f;
                for (int j = 0; j < adjacentCount[i]; j++)
                {
                    int idx = adjacentOffset[i] + j;
                    edgeLenght += Vector3.Magnitude(meshPositions[adjacentVertices[idx]] - meshPositions[i]);
                }

                restLengths[i] = edgeLenght / adjacentCount[i];
            }
        }
        
        public unsafe JobHandle CalculateEdgeLengthsJob(JobHandle inputDependencies, Vector3[] positions)
        {
            fixed (Vector3* meshPositions = positions)
            {
                var job = new ResolveEdgeLengthJob()
                {
                    MeshPositions = meshPositions,
                    EdgeRestLengths = tensionEdgeRestLengths,
                    AdjacentVertices = tensionAdjacentVertices,
                    AdjacentOffset = tensionAdjacentOffset,
                    AdjacentCount = tensionAdjacentCount,
                    K = tensionGain,
                    Colors = tensionColors
                };
                return job.Schedule(tensionMeshAdjacency.vertexCount, 128, inputDependencies);
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        unsafe struct ResolveEdgeLengthJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* MeshPositions;

            //[ReadOnly] public float intensity;
            [ReadOnly] public float K;
            [ReadOnly] public NativeArray<float> EdgeRestLengths;
            [ReadOnly] public NativeArray<int> AdjacentVertices;
            [ReadOnly] public NativeArray<int> AdjacentOffset;
            [ReadOnly] public NativeArray<int> AdjacentCount;

            [WriteOnly] public NativeArray<Color> Colors;

            float gain(float x, float k)
            {
                float a = 0.5f * Mathf.Pow(Mathf.Abs(2.0f * ((x < 0.5f) ? x : 1.0f - x)), k);
                return (x < 0.5f) ? a : 1.0f - a;
            }
            
            public void Execute(int i)
            {
                Colors[i] = Color.black;
                var edgeLenght = 0.0f;
                for (int j = 0; j < AdjacentCount[i]; j++)
                {
                    int idx = AdjacentOffset[i] + j;
                    edgeLenght += Vector3.Magnitude(MeshPositions[AdjacentVertices[idx]] - MeshPositions[i]);
                }

                var edgeDeltaValue = (((edgeLenght / AdjacentCount[i]) - EdgeRestLengths[i])) / EdgeRestLengths[i];
                var edgeDelta = (float) gain(Math.Abs(edgeDeltaValue), K) * Mathf.Sign(edgeDeltaValue);

                if (edgeDelta > 0.0f)
                {
                    Colors[i] = Color.Lerp(Color.black, Color.green, edgeDelta);
                }

                else
                {
                    Colors[i] = Color.Lerp(Color.black, Color.red, Math.Abs(edgeDelta));
                }
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


            if (tensionSampleEnable)
            {
                cmd.BeginSample("Skin Deformation Tension");

                int colorStream = meshInstance.GetVertexAttributeStream(VertexAttribute.Color);
                int[] colorStrideOffset =
                {
                    meshInstance.GetVertexBufferStride(colorStream),
                    meshInstance.GetVertexAttributeOffset(VertexAttribute.Color)
                };

                using GraphicsBuffer meshColorBuffer = meshInstance.GetVertexBuffer(colorStream);

                cmd.SetComputeIntParams(skinDeformCS, Uniforms._ColorStrideOffset, colorStrideOffset);

                cmd.SetComputeBufferParam(skinDeformCS, calculateSkinTensionKernel, Uniforms._EdgeRestLengthsBuffer,
                    edgeRestLengthsBuffer);
                cmd.SetComputeBufferParam(skinDeformCS, calculateSkinTensionKernel, Uniforms._AdjacentVerticesBuffer,
                    adjacentVerticesBuffer);
                cmd.SetComputeBufferParam(skinDeformCS, calculateSkinTensionKernel,
                    Uniforms._AdjacentVerticesCountBuffer, adjacentVerticesCountBuffer);
                cmd.SetComputeBufferParam(skinDeformCS, calculateSkinTensionKernel, Uniforms._AdjacentOffsetsBuffer,
                    adjacentOffsetsBuffer);
                cmd.SetComputeFloatParam(skinDeformCS, Uniforms._SkinTensionGain, tensionGain);

                cmd.SetComputeBufferParam(skinDeformCS, calculateSkinTensionKernel, Uniforms._TargetMeshPosNormalBuffer,
                    meshPosBuffer);
                cmd.SetComputeBufferParam(skinDeformCS, calculateSkinTensionKernel, Uniforms._TargetMeshColorBuffer,
                    meshColorBuffer);

                int dispatchCount = (meshAssetBuffers.vertexCount + groupSize - 1) / groupSize;
                cmd.DispatchCompute(skinDeformCS, calculateSkinTensionKernel, dispatchCount, 1, 1);

                cmd.EndSample("Skin Deformation Tension");
            }

            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        void EnsureGPUDeformationResources()
        {
            if (skinDeformCS == null || positionAndNormalDeltasBuffer == null ||
                (tensionSampleEnable && edgeRestLengthsBuffer == null))
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
            calculateSkinTensionKernel = skinDeformCS.FindKernel("CalculateSkinTension");

            const int packedPosNormEntrySizeBytes = 2 * 3 + 2 * 3;
            
            positionAndNormalDeltasBuffer = new ComputeBuffer(meshAssetBuffers.vertexCount, packedPosNormEntrySizeBytes, ComputeBufferType.Raw);
            positionAndNormalDeltaCPUBuffer = new NativeArray<half>(meshAssetBuffers.vertexCount * 6,
                Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            neutralPositionsBuffer = new ComputeBuffer(meshAssetBuffers.vertexCount, 4 * 3);
            neutralNormalsBuffer = new ComputeBuffer(meshAssetBuffers.vertexCount, 4 * 3);

            if (tensionSampleEnable)
            {
                edgeRestLengthsBuffer = new ComputeBuffer(tensionEdgeRestLengths.Length, sizeof(float),
                    ComputeBufferType.Structured);

                adjacentVerticesBuffer = new ComputeBuffer(tensionAdjacentVertices.Length, sizeof(int),
                    ComputeBufferType.Structured);
                ;
                adjacentVerticesCountBuffer = new ComputeBuffer(tensionAdjacentCount.Length, sizeof(int),
                    ComputeBufferType.Structured);
                ;
                adjacentOffsetsBuffer = new ComputeBuffer(tensionAdjacentOffset.Length, sizeof(int),
                    ComputeBufferType.Structured);
            }

            //upload static data
            neutralPositionsBuffer.SetData(meshAssetBuffers.vertexPositions);
            neutralNormalsBuffer.SetData(meshAssetBuffers.vertexNormals);

            if (tensionSampleEnable)
            {
                edgeRestLengthsBuffer.SetData(tensionEdgeRestLengths);
                adjacentVerticesBuffer.SetData(tensionAdjacentVertices);
                adjacentVerticesCountBuffer.SetData(tensionAdjacentCount);
                adjacentOffsetsBuffer.SetData(tensionAdjacentOffset);
            }

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

            if (tensionSampleEnable && edgeRestLengthsBuffer != null)
            {
                edgeRestLengthsBuffer.Dispose();
                adjacentVerticesBuffer.Dispose();
                adjacentVerticesCountBuffer.Dispose();
                adjacentOffsetsBuffer.Dispose();

                edgeRestLengthsBuffer = null;
                adjacentVerticesBuffer = null;
                adjacentVerticesCountBuffer = null;
                adjacentOffsetsBuffer = null;
            }
        }

#endif
#endregion
    }
}