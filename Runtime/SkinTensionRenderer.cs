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
    [ExecuteAlways]
    public class SkinTensionRenderer : MonoBehaviour
    {
        [Header("Tension Sampler")] public bool tensionSampleEnable = true;
        public float tensionGain = 1.0f;
        public Mesh tensionBasePose;
#if UNITY_2021_2_OR_NEWER
        public bool executeOnGPU = false;
#else
        private const bool executeOnGPU = false;
#endif
        [NonSerialized] private MeshAdjacency tensionMeshAdjacency;
        private MeshBuffers meshAssetBuffers;
        public bool ExecuteTensionCPU => !executeOnGPU;
        
        private JobHandle tensionStagingJob;
        private JobHandle tensionsRestStageJob;
        private NativeArray<float> tensionEdgeRestLengths;
        private NativeArray<int> tensionAdjacentVertices;
        private NativeArray<int> tensionAdjacentCount;
        private NativeArray<int> tensionAdjacentOffset;
        private NativeArray<float> skinTensionWeightsBuffer;
        private GraphicsBuffer skinTensionWeightsGPUBuffer;
        private bool resourcesCreatedForTensions;

        private Mesh lastTensionBasePose = null;
        private Mesh currentMesh = null;
        private SkinDeformationRenderer skinDeformRenderer = null;
        private SkinnedMeshRenderer smr = null;
        private MeshRenderer mr = null;
        private MeshFilter mf = null;
        private bool lastExecuteWasOnCPU = false;
        private MaterialPropertyBlock propertyBlock;
        
        static class Uniforms
        {
            internal static int _PosNormalBuffer = Shader.PropertyToID("_PosNormalBuffer");
            internal static int _TensionWeightsBuffer = Shader.PropertyToID("_TensionWeightsBuffer");
            internal static int _NumberOfVertices = Shader.PropertyToID("_NumberOfVertices");
            internal static int _PositionStrideOffset = Shader.PropertyToID("_PositionStrideOffset");
            internal static int _EdgeRestLengthsBuffer = Shader.PropertyToID("_EdgeRestLengthsBuffer");
            internal static int _AdjacentVerticesBuffer = Shader.PropertyToID("_AdjacentVerticesBuffer");
            internal static int _AdjacentVerticesCountBuffer = Shader.PropertyToID("_AdjacentVerticesCountBuffer");
            internal static int _AdjacentOffsetsBuffer = Shader.PropertyToID("_AdjacentOffsetsBuffer");
            internal static int _SkinTensionGain = Shader.PropertyToID("_SkinTensionGain");
            internal static int _SkinTensionDataValid = Shader.PropertyToID("_SkinTensionDataValid");
        }

#if UNITY_2021_2_OR_NEWER
        // GPU Deform resource
        private ComputeShader skinTensionCS;
        private int calculateSkinTensionKernel;
        private ComputeBuffer edgeRestLengthsBuffer;
        private ComputeBuffer adjacentVerticesBuffer;
        private ComputeBuffer adjacentVerticesCountBuffer;
        private ComputeBuffer adjacentOffsetsBuffer;
#endif
        private void OnEnable()
        {
            if (TryGetComponent(out SkinDeformationRenderer sdr))
            {
                sdr.afterSkinDeformationCallback -= ExecuteTension;
                sdr.afterSkinDeformationCallback += ExecuteTension;
                skinDeformRenderer = sdr;
            }

            if (!TryGetComponent(out smr))
            {
                TryGetComponent(out mr);
                TryGetComponent(out mf);
            }
            lastExecuteWasOnCPU = ExecuteTensionCPU;
            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            if (TryGetComponent(out SkinDeformationRenderer sdr))
            {
                sdr.afterSkinDeformationCallback -= ExecuteTension;
            }

            FreeResources();
            EnableTensionForMaterials(false);
        }

        void LateUpdate()
        {
            //if there is skindeformation renderer present, we will use it's after deform hook to execute the tension calculations
            if (skinDeformRenderer != null) return;
            ExecuteTension();
        }
        

        void ExecuteTension()
        {
            if (!tensionSampleEnable) return;
            if (!EnsureMeshAndResources()) return;
            
            if (ExecuteTensionCPU)
            {
                UnityEngine.Profiling.Profiler.BeginSample("Tension: LateUpdate");
                tensionStagingJob = CalculateEdgeLengthsJob(tensionsRestStageJob, skinDeformRenderer ? skinDeformRenderer.CurrentDeformedPositions : currentMesh.vertices);
                tensionStagingJob.Complete();
                skinTensionWeightsGPUBuffer.SetData(skinTensionWeightsBuffer);
                UnityEngine.Profiling.Profiler.EndSample();
            }
#if UNITY_2021_2_OR_NEWER
            else
            {
                ExecuteTensionGPU();
            }
#endif
            EnableTensionForMaterials(tensionSampleEnable);
        }

        void EnableTensionForMaterials(bool enable)
        {
            
            if (smr != null)
            {
                smr.GetPropertyBlock(propertyBlock);
            } 
            else if (mr != null)
            {
                mr.GetPropertyBlock(propertyBlock);
            }

            if (enable)
            {
                propertyBlock.SetInt(Uniforms._SkinTensionDataValid, 1);
                propertyBlock.SetBuffer(Uniforms._TensionWeightsBuffer, skinTensionWeightsGPUBuffer);
            }
            else
            {
                propertyBlock.SetInt(Uniforms._SkinTensionDataValid, 0);
            }
            
            if (smr != null)
            {
                smr.SetPropertyBlock(propertyBlock);
            } else if (mr != null)
            {
                mr.SetPropertyBlock(propertyBlock);
            }
        }

        bool EnsureMeshAndResources()
        {
            Mesh m = null;
            if (smr && smr.sharedMesh)
            {
                m = smr.sharedMesh;
            } else if (mf && mf.sharedMesh)
            {
                m = mf.sharedMesh;
            }
            if (m == null) return false;
            bool success = true;
            if (m != currentMesh || lastTensionBasePose != tensionBasePose || lastExecuteWasOnCPU != ExecuteTensionCPU || skinTensionWeightsGPUBuffer == null)
            {
                currentMesh = m;
                lastTensionBasePose = tensionBasePose;
                lastExecuteWasOnCPU = ExecuteTensionCPU;
                
                MeshChanged();
                FreeResources();
                success = CreateResources();
            }

            return success;
        }


        void MeshChanged()
        {
            if (meshAssetBuffers == null)
                meshAssetBuffers = new MeshBuffers(currentMesh);
            else
                meshAssetBuffers.LoadFrom(currentMesh);

            if (tensionMeshAdjacency == null)
                tensionMeshAdjacency = new MeshAdjacency(meshAssetBuffers, true);
            else
                tensionMeshAdjacency.LoadFrom(meshAssetBuffers);
        }

        bool CreateResources()
        {
            bool success = true;
            BuildAdjVertexArrays();
#if UNITY_2021_2_OR_NEWER
            if (!ExecuteTensionCPU)
            {
                success = EnsureGPUTensionResources();
            }
#endif
            if (ExecuteTensionCPU)
            {
                skinTensionWeightsBuffer = new NativeArray<float>(currentMesh.vertexCount, Allocator.Persistent);
            }
            skinTensionWeightsGPUBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, currentMesh.vertexCount, sizeof(float));
            
            return success;
        }

        void FreeResources()
        {
#if UNITY_2021_2_OR_NEWER
            DestroyGPUTensionResources();
#endif
            DestroyAdjVertexArrays();

            if (skinTensionWeightsGPUBuffer != null)
            {
                skinTensionWeightsGPUBuffer.Dispose();
                skinTensionWeightsGPUBuffer = null;
            }
        }


        void BuildAdjVertexArrays()
        {
            DestroyAdjVertexArrays();
            // Initialize
            tensionEdgeRestLengths = new NativeArray<float>(tensionMeshAdjacency.vertexCount, Allocator.Persistent);
            
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
            if (skinTensionWeightsBuffer.IsCreated)
            {
                skinTensionWeightsBuffer.Dispose();
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
            Vector3[] positions;
            if (tensionBasePose)
            {
                positions = tensionBasePose.vertices;
            }
            else
            {
                positions = currentMesh.vertices;
                
            }

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
                    Weights = skinTensionWeightsBuffer
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

            [WriteOnly] public NativeArray<float> Weights;

            float gain(float x, float k)
            {
                float a = 0.5f * Mathf.Pow(Mathf.Abs(2.0f * ((x < 0.5f) ? x : 1.0f - x)), k);
                return (x < 0.5f) ? a : 1.0f - a;
            }

            public void Execute(int i)
            {
                Weights[i] = 0;
                var edgeLenght = 0.0f;
                for (int j = 0; j < AdjacentCount[i]; j++)
                {
                    int idx = AdjacentOffset[i] + j;
                    edgeLenght += Vector3.Magnitude(MeshPositions[AdjacentVertices[idx]] - MeshPositions[i]);
                }

                var edgeDeltaValue = (((edgeLenght / AdjacentCount[i]) - EdgeRestLengths[i])) / EdgeRestLengths[i];
                var edgeDelta = (float) gain(Math.Abs(edgeDeltaValue), K) * Mathf.Sign(edgeDeltaValue);

                Weights[i] = edgeDelta;
            }
        }


        #region GPUTension

#if UNITY_2021_2_OR_NEWER

        bool EnsureGPUTensionResources()
        {
            if (skinTensionCS == null || edgeRestLengthsBuffer == null)
            {
                return CreateGPUTensionResources();
            }

            return true;
        }

        bool CreateGPUTensionResources()
        {
            DestroyGPUTensionResources();

            if (skinTensionCS == null)
            {
                skinTensionCS = Resources.Load<ComputeShader>("SkinTensionCS");
            }

            if (skinTensionCS == null)
            {
                return false;
            }

            calculateSkinTensionKernel = skinTensionCS.FindKernel("CalculateSkinTension");


            if (!ExecuteTensionCPU)
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


            edgeRestLengthsBuffer.SetData(tensionEdgeRestLengths);
            adjacentVerticesBuffer.SetData(tensionAdjacentVertices);
            adjacentVerticesCountBuffer.SetData(tensionAdjacentCount);
            adjacentOffsetsBuffer.SetData(tensionAdjacentOffset);
            

            currentMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;

            return true;
        }

        void DestroyGPUTensionResources()
        {

            if (edgeRestLengthsBuffer != null)
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


        void ExecuteTensionGPU()
        {
            const int groupSize = 64;

            int posStream = currentMesh.GetVertexAttributeStream(VertexAttribute.Position);

            int[] posStrideOffset =
            {
                currentMesh.GetVertexBufferStride(posStream),
                currentMesh.GetVertexAttributeOffset(VertexAttribute.Position)
            };

            using GraphicsBuffer meshPosBuffer = currentMesh.GetVertexBuffer(posStream);

            CommandBuffer cmd = CommandBufferPool.Get("Skin Tension");

            int vertexCount = tensionMeshAdjacency.vertexCount;
            
            cmd.SetComputeIntParam(skinTensionCS, Uniforms._NumberOfVertices, vertexCount);

            cmd.SetComputeIntParams(skinTensionCS, Uniforms._PositionStrideOffset, posStrideOffset);
            
            cmd.BeginSample("Skin Deformation Tension");

            cmd.SetComputeBufferParam(skinTensionCS, calculateSkinTensionKernel, Uniforms._EdgeRestLengthsBuffer,
                edgeRestLengthsBuffer);
            cmd.SetComputeBufferParam(skinTensionCS, calculateSkinTensionKernel, Uniforms._AdjacentVerticesBuffer,
                adjacentVerticesBuffer);
            cmd.SetComputeBufferParam(skinTensionCS, calculateSkinTensionKernel,
                Uniforms._AdjacentVerticesCountBuffer, adjacentVerticesCountBuffer);
            cmd.SetComputeBufferParam(skinTensionCS, calculateSkinTensionKernel, Uniforms._AdjacentOffsetsBuffer,
                adjacentOffsetsBuffer);
            cmd.SetComputeFloatParam(skinTensionCS, Uniforms._SkinTensionGain, tensionGain);

            cmd.SetComputeBufferParam(skinTensionCS, calculateSkinTensionKernel,
                Uniforms._PosNormalBuffer,
                meshPosBuffer);
            cmd.SetComputeBufferParam(skinTensionCS, calculateSkinTensionKernel, Uniforms._TensionWeightsBuffer,
                skinTensionWeightsGPUBuffer);

            int dispatchCount = (vertexCount + groupSize - 1) / groupSize;
            cmd.DispatchCompute(skinTensionCS, calculateSkinTensionKernel, dispatchCount, 1, 1);

            cmd.EndSample("Skin Deformation Tension");
            
 
            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

#endif
#endregion
    }
}
