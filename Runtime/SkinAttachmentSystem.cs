using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;
    public interface ISkinAttachment
    {
        public Renderer GetTargetRenderer();
        //called when subject has been updated (if update was on cpu, cmd is null, if on gpu, cmd is commandbuffer that contains the commands to update the subject on gpu)
        public void NotifyAttachmentResolved(CommandBuffer cmd);
        public void NotifyAllAttachmentsFromQueueResolved();
        
        bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescGPU desc);
        bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescCPU desc);
    }
    
    public static partial class SkinAttachmentSystem
    {
        public static Instance Inst
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = new Instance();
                }

                return s_instance;
            }
        }

        private static ComputeShader s_resolveAttachmentsCS;
        private static int s_resolveAttachmentsPosKernel = 0;
        private static int s_resolveAttachmentsPosNormalKernel = 0;
        private static int s_resolveAttachmentsPosNormalMovecKernel = 0;

        private static bool s_initialized = false;
        private static Instance s_instance;
        
        
        static SkinAttachmentSystem()
        {
            if (s_initialized == false)
            {
                if (s_resolveAttachmentsCS == null)
                {
                    s_resolveAttachmentsCS = Resources.Load<ComputeShader>("SkinAttachmentCS");
                }

                s_resolveAttachmentsPosKernel = s_resolveAttachmentsCS.FindKernel("ResolveAttachmentPositions");
                s_resolveAttachmentsPosNormalKernel =
                    s_resolveAttachmentsCS.FindKernel("ResolveAttachmentPositionsNormals");
                s_resolveAttachmentsPosNormalMovecKernel =
                    s_resolveAttachmentsCS.FindKernel("ResolveAttachmentPositionsNormalsMovecs");
                


                RenderPipelineManager.beginContextRendering += AfterGPUSkinning;
                s_initialized = true;
            }
        }
        
        public static bool IsValidAttachmentTarget(Renderer r)
        {
            if (r == null) return false;
            return r is SkinnedMeshRenderer || r is MeshRenderer;
        }


        static void AfterGPUSkinning(ScriptableRenderContext context, List<Camera> cameras)
        {
            IScheduleAttachmentHooks hooks = Inst;
            hooks.OnAfterGPUSkinning();
        }

        private interface IScheduleAttachmentHooks
        {
            void OnAfterGPUSkinning();
        }
        
        public class Instance : IScheduleAttachmentHooks
        {
            internal class AttachmentTargetData
            {
                //runtime (cpu) dependencies
                public Mesh lastSeenRuntimeMesh;
                public MeshBuffers meshBuffers;
                public int lastTargetUsedFrame;
                public int lastMeshBuffersUpdatedFrame;
                public Func<Mesh, bool> customBakeFunc;
                
                //bake dependencies 
                public MeshInfo meshInfo;
                public Hash128 bakeMeshInfoHash;
            }

            private Dictionary<Renderer, AttachmentTargetData> attachmentTargetDict =
                new Dictionary<Renderer, AttachmentTargetData>();

            private AttachmentResolveQueue attachmentResolveQueueGPU = new AttachmentResolveQueue();
            private AttachmentResolveQueue attachmentResolveQueueCPU = new AttachmentResolveQueue();
            private AttachmentResolveQueue explicitAttachmentResolveQueueGPU = new AttachmentResolveQueue();
            private AttachmentResolveQueue explicitAttachmentResolveQueueCPU = new AttachmentResolveQueue();


            void IScheduleAttachmentHooks.OnAfterGPUSkinning()
            {
                //TODO: should cpu resolve be moved somewhere else? This ensures that any changes done in late update is taken into account
                ResolveAttachmentsCPU(attachmentResolveQueueCPU);
                ResolveAttachmentsGPU(attachmentResolveQueueGPU);
                PruneUnusedAttachmentTargets();
            }

            
            
            public void QueueExplicitAttachmentResolve(ISkinAttachment sam, bool resolveOnGPU)
            {
                if (!MarkRendererUsed(sam.GetTargetRenderer())) return;
                
                if (resolveOnGPU)
                {
                    explicitAttachmentResolveQueueGPU.Add(sam);
                }
                else
                {
                    explicitAttachmentResolveQueueCPU.Add(sam);
                }
            }

            public void ExecuteExplicitAttachmentResolveCPU()
            {
                ResolveAttachmentsCPU(explicitAttachmentResolveQueueCPU);
            }
            
            public void ExecuteExplicitAttachmentResolveGPU()
            {
                ResolveAttachmentsGPU(explicitAttachmentResolveQueueGPU);
            }

            public void QueueAttachmentResolve(ISkinAttachment sam, bool resolveOnGPU)
            {
                if (!MarkRendererUsed(sam.GetTargetRenderer())) return;

                if (resolveOnGPU)
                {
                    attachmentResolveQueueGPU.Add(sam);
                }
                else
                {
                    attachmentResolveQueueCPU.Add(sam);
                }
            }

            public void SetCustomCPUSkinnedMeshBaker(Renderer r, Func<Mesh, bool> customBake)
            {
                AttachmentTargetData data = GetAttachmentTargetData(r);
                data.customBakeFunc = customBake;
            }
            
            public bool GetAttachmentTargetMeshInfo(Renderer r, out MeshInfo info, bool allowReadback, Mesh explicitBakeMesh)
            {
                info = new MeshInfo();
                if (IsValidAttachmentTarget(r))
                {
                    AttachmentTargetData data = GetAttachmentTargetData(r);
                    
                    Mesh m = GetPoseBakeMesh(r, explicitBakeMesh, allowReadback);
                    if (m == null) return false;
                    
                    Hash128 bakeMeshHash = GetBakeMeshHash(m);
                    bool oldBakeDataValid = data.bakeMeshInfoHash == bakeMeshHash;
                    data.bakeMeshInfoHash = bakeMeshHash;

                    //is bakedata up to date?
                    if (!oldBakeDataValid)
                    {
                        const bool weldedAdjacency = false;

                        if (data.meshInfo.meshBuffers == null)
                            data.meshInfo.meshBuffers = new MeshBuffers(m);
                        else
                            data.meshInfo.meshBuffers.LoadFrom(m);
                        
                        if (data.meshInfo.meshAdjacency == null)
                            data.meshInfo.meshAdjacency = new MeshAdjacency(data.meshInfo.meshBuffers, weldedAdjacency);
                        else 
                            data.meshInfo.meshAdjacency.LoadFrom(data.meshInfo.meshBuffers, weldedAdjacency);
                        
                        if (data.meshInfo.meshVertexBSP == null)
                            data.meshInfo.meshVertexBSP = new KdTree3(data.meshInfo.meshBuffers.vertexPositions, data.meshInfo.meshBuffers.vertexCount);
                        else
                            data.meshInfo.meshVertexBSP.BuildFrom(data.meshInfo.meshBuffers.vertexPositions, data.meshInfo.meshBuffers.vertexCount);
                        
                    }
                    info = data.meshInfo;
                }
                
                return true;
            }
            
            public Mesh GetPoseBakeMesh(Renderer r, Mesh explicitBakeMesh, bool allowGPUReadback)
            {
                if (!IsValidAttachmentTarget(r)) return null;
                
                Mesh bakeMesh = explicitBakeMesh;

                if (bakeMesh == null)
                {
                    if (r is MeshRenderer mr)
                    {
                        if (!mr.TryGetComponent(out MeshFilter mf)) return null;
                      
                        if (mf.sharedMesh != null)
                        {
                            if (allowGPUReadback)
                            {
                                bakeMesh = Object.Instantiate(mf.sharedMesh);
                                bakeMesh.name = "SkinAttachmentTarget(BakeMesh)";
                                bakeMesh.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;
                                bakeMesh.MarkDynamic();
                                ReadbackMeshData(mf.sharedMesh, bakeMesh);
                            }
                            else
                            {
                                bakeMesh = mf.sharedMesh;
                            }
                            
                        }
                    }
                
                    if(r is SkinnedMeshRenderer smr)
                    {
                        if (smr != null)
                        {
                            bakeMesh = Object.Instantiate(smr.sharedMesh);
                            bakeMesh.name = "SkinAttachmentTarget(BakeMesh)";
                            bakeMesh.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;
                            bakeMesh.MarkDynamic();
                            
                            AttachmentTargetData data = GetAttachmentTargetData(r);
                            
                            Profiler.BeginSample("smr.BakeMesh");
                            {
                                var result = data.customBakeFunc?.Invoke(bakeMesh);
                                if (!result.HasValue || !result.Value)
                                {
                                    smr.BakeMesh(bakeMesh);
                                }
                                smr.BakeMesh(bakeMesh);
                                {
                                    bakeMesh.bounds = smr.bounds;
                                }
                            }
                            Profiler.EndSample();
                        }
                    }
                }

                return bakeMesh;

            }

            static void CopyReadbackDataToArray<T>(T[] targetBuffer, NativeArray<byte> readbackBuffer,int vertexCount, int stride, int offset, int size) where T: unmanaged
            {
                unsafe
                {
                    byte* srcPtr = (byte*)readbackBuffer.GetUnsafePtr();
                    fixed (T* targetPtr = targetBuffer)
                    {
                        for (int i = 0; i < vertexCount; ++i)
                        {
                            int srcOffset = stride * i + offset;
                            UnsafeUtility.MemCpy(targetPtr + i, srcPtr + srcOffset, size);
                        }
                            
                    }
                }
            }
            
            void ReadbackMeshData(Mesh src, Mesh target)
            {
                int positionStream = src.GetVertexAttributeStream(VertexAttribute.Position);
                int normalStream = src.GetVertexAttributeStream(VertexAttribute.Normal);
                int tangentStream = src.GetVertexAttributeStream(VertexAttribute.Tangent);
                
                using GraphicsBuffer skinPositionsBuffer = src.GetVertexBuffer(positionStream);
                using GraphicsBuffer skinNormalsBuffer = src.GetVertexBuffer(normalStream);
                using GraphicsBuffer skinTangentsBuffer = src.GetVertexBuffer(tangentStream);
                
                var posOffset = src.GetVertexAttributeOffset(VertexAttribute.Position);
                var posStride = src.GetVertexBufferStride(positionStream);
                
                var normOffset = src.GetVertexAttributeOffset(VertexAttribute.Normal);
                var normStride = src.GetVertexBufferStride(normalStream);
                
                var tanOffset = src.GetVertexAttributeOffset(VertexAttribute.Tangent);
                var tanStride = src.GetVertexBufferStride(tangentStream);

                Dictionary<int, NativeArray<byte>> readbackData = new Dictionary<int, NativeArray<byte>>();
                Dictionary<int, GraphicsBuffer> gpuBuffers = new Dictionary<int, GraphicsBuffer>();
                if (skinPositionsBuffer != null)
                {
                    readbackData[positionStream] = new NativeArray<byte>(skinPositionsBuffer.stride * skinPositionsBuffer.count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    gpuBuffers[positionStream] = skinPositionsBuffer;
                }
                
                if (normalStream != positionStream && skinNormalsBuffer != null)
                {
                    readbackData[normalStream] = new NativeArray<byte>(skinNormalsBuffer.stride * skinNormalsBuffer.count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    gpuBuffers[normalStream] = skinNormalsBuffer;
                }
                
                if (tangentStream != positionStream && tangentStream != normalStream && skinTangentsBuffer != null)
                {
                    readbackData[tangentStream] = new NativeArray<byte>(skinTangentsBuffer.stride * skinTangentsBuffer.count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    gpuBuffers[tangentStream] = skinTangentsBuffer;
                }
                
                int vertexCount = src.vertexCount;
                
                foreach (var r in readbackData)
                {
                    var readbackBuffer = r.Value;
                    AsyncGPUReadback.RequestIntoNativeArray(ref readbackBuffer, gpuBuffers[r.Key]);
                }
                AsyncGPUReadback.WaitAllRequests();

                if (skinPositionsBuffer != null)
                {
                    var readbackBuffer = readbackData[positionStream];
                    var vertices = new Vector3[vertexCount];
                    CopyReadbackDataToArray(vertices, readbackBuffer, vertexCount, posStride, posOffset, UnsafeUtility.SizeOf<Vector3>());
                    target.vertices = vertices;
                }
                
                if (skinNormalsBuffer != null)
                {
                    var readbackBuffer = readbackData[normalStream];
                    var normals = new Vector3[vertexCount];
                    CopyReadbackDataToArray(normals, readbackBuffer, vertexCount, normStride, normOffset, UnsafeUtility.SizeOf<Vector3>());
                    target.normals = normals;
                }
                
                if (skinTangentsBuffer != null)
                {
                    var readbackBuffer = readbackData[tangentStream];
                    var tan = new Vector4[vertexCount];
                    CopyReadbackDataToArray(tan, readbackBuffer, vertexCount, tanStride, tanOffset, UnsafeUtility.SizeOf<Vector4>());
                    target.tangents = tan;
                }

                foreach (var r in readbackData)
                {
                    r.Value.Dispose();
                }
            }
            
            bool MarkRendererUsed(Renderer r)
            {
                if (!IsValidAttachmentTarget(r))
                {
                    return false;
                }

                //we don't really use the data here but call this to mark the renderer being used 
                GetAttachmentTargetData(r);

                return true;
            }

            Hash128 GetBakeMeshHash(Mesh m)
            {
                Hash128 hash = default;
                
                Vector3[] positions = m.vertices;
                Vector4[] tangents = m.tangents;
                Vector3[] normals = m.normals;
                
                hash.Append(positions);
                hash.Append(tangents);
                hash.Append(normals);

                for (int i = 0; i < m.subMeshCount; ++i)
                {
                    hash.Append(m.GetIndices(i));
                }

                return hash;

            }
            
            void PruneUnusedAttachmentTargets()
            {
                const int framesToDelete = 120;
                int frameCount = Time.frameCount;
                List<Renderer> entriesToRemove = new List<Renderer>();
                foreach (var entry in attachmentTargetDict)
                {
                    if ((frameCount - entry.Value.lastTargetUsedFrame) > framesToDelete)
                    {
                        entriesToRemove.Add(entry.Key);
                    }
                }

                foreach (var removeEntry in entriesToRemove)
                {
                    attachmentTargetDict.Remove(removeEntry);
                }
            }
            
            

            bool EnsureRuntimeMeshBuffers(Renderer renderer, AttachmentTargetData attachmentTargetData)
            {
                if (!IsValidAttachmentTarget(renderer)) return false;

                Mesh runtimeMesh = attachmentTargetData.lastSeenRuntimeMesh;
                int currentFrame = Time.frameCount;
                //TODO: is it valid to assume that the mesh cannot have changed in the middle of the frame?
                if (attachmentTargetData.lastMeshBuffersUpdatedFrame == currentFrame && runtimeMesh != null)
                {
                    return true;
                }
                
                if (renderer is MeshRenderer mr)
                {
                    if (!mr.TryGetComponent(out MeshFilter mf)) return false;
                    if (mf.sharedMesh != null)
                    {
                        runtimeMesh = mf.sharedMesh;
                    }
                }
                
                if(renderer is SkinnedMeshRenderer smr)
                {
                    if (smr != null)
                    {
                        if (runtimeMesh == null)
                        {
                            runtimeMesh = Object.Instantiate(smr.sharedMesh);
                            runtimeMesh.name = "SkinAttachmentTarget(BakeMeshRuntime)";
                            runtimeMesh.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;
                            runtimeMesh.MarkDynamic();
                        }
                        Profiler.BeginSample("smr.BakeMesh");
                        {
                            smr.BakeMesh(runtimeMesh);
                            {
                                runtimeMesh.bounds = smr.bounds;
                            }
                        }
                        Profiler.EndSample();
                    }
                }

                if (!runtimeMesh.HasVertexAttribute(VertexAttribute.Tangent))
                {
                    Debug.LogError(
                            "SkinAttachment target (SkinnedMeshRenderer) does not have tangents, the attachment resolve will not be correct!");
                }

                attachmentTargetData.lastSeenRuntimeMesh = runtimeMesh;
                attachmentTargetData.lastMeshBuffersUpdatedFrame = currentFrame;
                if (attachmentTargetData.meshBuffers == null)
                    attachmentTargetData.meshBuffers = new MeshBuffers(runtimeMesh);
                else
                    attachmentTargetData.meshBuffers.LoadFrom(runtimeMesh);
                    
                return true;
            }
            
            void ResolveAttachmentsCPU(AttachmentResolveQueue queue)
            {
                if (queue.Empty()) return;
                
                KeyValuePair<Renderer, List<ISkinAttachment>>[] attachmentsPerRenderer =
                    queue.GetAttachmentsPerRenderer();
                
                queue.Clear();

                List<SkinAttachmentDescCPU> attachmentDescs = new List<SkinAttachmentDescCPU>();

                foreach (var rendererEntry in attachmentsPerRenderer)
                {
                    Renderer renderer = rendererEntry.Key;
                    List<ISkinAttachment> attachments = rendererEntry.Value;
                    attachmentDescs.Clear();
                    

                    //fill skin attachment target desc
                    AttachmentTargetData attachmentTargetData = GetAttachmentTargetData(renderer);
                    if (!EnsureRuntimeMeshBuffers(renderer, attachmentTargetData))
                        continue;
                    
                    SkinAttachmentTargetDescCPU attachmentTargetDesc = default;
                    attachmentTargetDesc.positions = attachmentTargetData.meshBuffers.vertexPositions;
                    attachmentTargetDesc.normals = attachmentTargetData.meshBuffers.vertexNormals;
                    attachmentTargetDesc.tangents = attachmentTargetData.meshBuffers.vertexTangents;
                    

                    //gather attachments
                    foreach (var att in attachments)
                    {
                        SkinAttachmentDescCPU descCPU = default;
                        if (att.FillSkinAttachmentDesc(ref descCPU))
                        {
                            attachmentDescs.Add(descCPU);
                        }
                    }

                    if (attachmentDescs.Count > 0)
                    {
                        ResolveSubjectsCPU(ref attachmentTargetDesc, attachmentDescs.ToArray());
                    }

                    //notify attachments about being updated (this potentially will also notify attachments that failed to produce desc, should these be filtered out(?)
                    foreach (var attachment in attachments)
                    {
                        attachment.NotifyAttachmentResolved(null);
                    }
                }

                //notify when all attachments resolved and commands are actually submitted
                foreach (var attPerRenderer in attachmentsPerRenderer)
                {
                    foreach (var att in attPerRenderer.Value)
                    {
                        att.NotifyAllAttachmentsFromQueueResolved();
                    }
                    
                }
                
                
            }

            private void ResolveAttachmentsGPU(AttachmentResolveQueue queue)
            {
                if (queue.Empty()) return;
                
                KeyValuePair<Renderer, List<ISkinAttachment>>[] attachmentsPerRenderer =
                    queue.GetAttachmentsPerRenderer();

                List<SkinAttachmentDescGPU> attachmentDescs = new List<SkinAttachmentDescGPU>();
                queue.Clear();

                CommandBuffer cmd = CommandBufferPool.Get("Resolve SkinAttachments");

                foreach (var rendererEntry in attachmentsPerRenderer)
                {
                    Renderer renderer = rendererEntry.Key;
                    List<ISkinAttachment> attachments = rendererEntry.Value;
                    attachmentDescs.Clear();

                    SkinAttachmentTargetDescGPU attachmentTargetDesc = default;

                    if (!FillSkinAttachmentTargetDescFromRenderer(renderer, ref attachmentTargetDesc))
                        continue;

                    //gather attachments
                    foreach (var att in attachments)
                    {
                        SkinAttachmentDescGPU descGPU = default;
                        if (att.FillSkinAttachmentDesc(ref descGPU))
                        {
                            attachmentDescs.Add(descGPU);
                        }
                    }

                    //resolve attachments
                    if (attachmentDescs.Count > 0)
                    {
                        ResolveSubjectsGPU(cmd, ref attachmentTargetDesc, attachmentDescs.ToArray());
                    }

                    //free descs
                    foreach (var desc in attachmentDescs)
                    {
                        FreeSkinAttachmentDesc(desc);
                    }

                    FreeSkinAttachmentTargetDesc(attachmentTargetDesc);

                    //notify attachments about being updated (this potentially will also notify attachments that failed to produce desc, should these be filtered out(?)
                    foreach (var attachment in attachments)
                    {
                        attachment.NotifyAttachmentResolved(cmd);
                    }
                }

                Graphics.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                //notify when all attachments resolved and commands are actually submitted
                foreach (var attPerRenderer in attachmentsPerRenderer)
                {
                    foreach (var att in attPerRenderer.Value)
                    {
                        att.NotifyAllAttachmentsFromQueueResolved();
                    }
                    
                }
                
                
            }

            private AttachmentTargetData GetAttachmentTargetData(Renderer target)
            {
                if (attachmentTargetDict.TryGetValue(target, out var data))
                {
                    data.lastTargetUsedFrame = Time.frameCount;
                    return data;
                }
                else
                {
                    AttachmentTargetData newData = new AttachmentTargetData
                    {
                        lastTargetUsedFrame = Time.frameCount,
                        lastMeshBuffersUpdatedFrame = -1,
                        bakeMeshInfoHash = default
                    };
                    attachmentTargetDict[target] = newData;
                    return newData;
                }
            }

            bool FillSkinAttachmentTargetDescFromRenderer(Renderer r, ref SkinAttachmentTargetDescGPU desc)
            {
                if (r is SkinnedMeshRenderer meshRenderer)
                {
                    return FillSkinAttachmentTargetDesc(meshRenderer, ref desc);
                }

                if (r is MeshRenderer renderer)
                {
                    renderer.TryGetComponent(out MeshFilter mf);
                    return FillSkinAttachmentTargetDesc(renderer, mf, ref desc);
                }

                return false;
            }

            
        }

        internal class AttachmentResolveQueue
        {
            private Dictionary<Renderer, List<ISkinAttachment>> meshAttachmentsQueue = new();

            public void Add(ISkinAttachment sam)
            {
                Renderer r = sam.GetTargetRenderer();
                List<ISkinAttachment> attachmentList;
                if (meshAttachmentsQueue.TryGetValue(r, out attachmentList))
                {
                    attachmentList.Add(sam);
                }
                else
                {
                    meshAttachmentsQueue[r] = new List<ISkinAttachment> { sam };
                }
            }

            public bool Empty()
            {
                return meshAttachmentsQueue.Count == 0;
            }
            
            public KeyValuePair<Renderer, List<ISkinAttachment>>[] GetAttachmentsPerRenderer()
            {
                return meshAttachmentsQueue.ToArray();
            }

            public void Clear()
            {
                meshAttachmentsQueue.Clear();
            }
        }
    }
}