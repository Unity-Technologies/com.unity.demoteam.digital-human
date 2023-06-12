using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;
    public interface ISkinAttachment
    {
        public Renderer GetTargetRenderer();

        //called when subject has been updated (if update was on cpu, cmd is null, if on gpu, cmd is commandbuffer that contains the commands to update the subject on gpu)
        public void NotifyAttachmentUpdated(CommandBuffer cmd);
    }

    public interface ISkinAttachmentMesh : ISkinAttachment
    {
        bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescGPU desc);
        bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescCPU desc);
    };

    public interface ISkinAttachmentTransform : ISkinAttachment
    {
        bool GetSkinAttachmentPosesAndItem(out SkinAttachmentPose[] poses, out SkinAttachmentItem item);
        Matrix4x4 GetResolveTransform();
        Vector3[] GetPositionResolveBufferCPU();
        Vector3[] GetNormalResolveBufferCPU();
    };

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

        public static int TransformAttachmentBufferStride => c_transformAttachmentBufferStride;
        
        private static ComputeShader s_resolveAttachmentsCS;
        private static int s_resolveAttachmentsPosKernel = 0;
        private static int s_resolveAttachmentsPosNormalKernel = 0;
        private static int s_resolveAttachmentsPosNormalMovecKernel = 0;

        private static bool s_initialized = false;
        private static Instance s_instance;
        private const int c_transformAttachmentBufferStride = 3 * sizeof(float);
        
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
		[RuntimeInitializeOnLoadMethod]
#endif
        static void StaticInitialize()
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
            void OnAfterLateUpdate();
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
                
                //bake dependencies 
                public Mesh lastSeenBakeMesh;
                public MeshInfo meshInfo;
                public int lastBakeDataUpdatedFrame;
            }

            private Dictionary<Renderer, AttachmentTargetData> attachmentTargetDict =
                new Dictionary<Renderer, AttachmentTargetData>();

            private AttachmentResolveQueue attachmentResolveQueueGPU = new AttachmentResolveQueue();
            private AttachmentResolveQueue attachmentResolveQueueCPU = new AttachmentResolveQueue();
            private int requestedGPUTransformResolvesForFrame = 0;
            private GraphicsBuffer transformResolveGraphicsBuffer;

            ~Instance()
            {
                if (transformResolveGraphicsBuffer != null && transformResolveGraphicsBuffer.IsValid())
                {
                    transformResolveGraphicsBuffer.Release();
                    transformResolveGraphicsBuffer = null;
                }
            }

            void IScheduleAttachmentHooks.OnAfterGPUSkinning()
            {
                //TODO: should only resolve gpu here, for now also cpu
                ResolveAttachmentsCPU(attachmentResolveQueueCPU);
                ResolveAttachmentsGPU(attachmentResolveQueueGPU);
                PruneUnusedAttachmentTargets();
                requestedGPUTransformResolvesForFrame = 0;
            }

            void IScheduleAttachmentHooks.OnAfterLateUpdate()
            {
                
            }

            public void QueueAttachmentResolve(ISkinAttachment sam, bool resolveOnGPU)
            {
                Renderer r = sam.GetTargetRenderer();

                if (!IsValidAttachmentTarget(r))
                {
                    return;
                }

                //we don't really use the data here but call this to mark the renderer being used 
                GetAttachmentTargetData(r);

                if (resolveOnGPU)
                {
                    attachmentResolveQueueGPU.Add(sam);
                }
                else
                {
                    attachmentResolveQueueCPU.Add(sam);
                }
            }

            public int requestGPUTransformResolveForFrame()
            {
                return requestedGPUTransformResolvesForFrame++;
            }

            public bool GetAttachmentTargetMeshInfo(Renderer r, out MeshInfo info, Mesh explicitBakeMesh = null)
            {
                info = new MeshInfo();
                if (IsValidAttachmentTarget(r))
                {
                    AttachmentTargetData data = GetAttachmentTargetData(r);

                    int currentFrame = Time.frameCount;
                    
                    Mesh m = GetPoseBakeMesh(r, explicitBakeMesh);
                    if (m == null) return false;
                    //TODO: is it valid to assume that the mesh cannot have changed in the middle of the frame?
                    bool oldBakeDataValid = data.lastBakeDataUpdatedFrame == currentFrame && m == data.lastSeenBakeMesh;
                    data.lastSeenBakeMesh = m;

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

                        data.lastBakeDataUpdatedFrame = currentFrame;
                    }
                    info = data.meshInfo;
                }
                
                return true;
            }

            void EnsureTransformGPUResources()
            {
                if (transformResolveGraphicsBuffer.count < requestedGPUTransformResolvesForFrame)
                {
                    transformResolveGraphicsBuffer.Release();
                    transformResolveGraphicsBuffer = null;
                }

                if (transformResolveGraphicsBuffer == null)
                {
                    transformResolveGraphicsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw,
                        requestedGPUTransformResolvesForFrame, TransformAttachmentBufferStride);
                }
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
            
            Mesh GetPoseBakeMesh(Renderer r, Mesh explicitBakeMesh = null)
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
                            bakeMesh = mf.sharedMesh;
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
                            
                            Profiler.BeginSample("smr.BakeMesh");
                            {
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

                List<SkinAttachmentDescCPU> attachmentDescs = new List<SkinAttachmentDescCPU>();
                List<ISkinAttachmentMesh> meshAttachments = new List<ISkinAttachmentMesh>();
                List<ISkinAttachmentTransform> pointsAttachments = new List<ISkinAttachmentTransform>();

                foreach (var rendererEntry in attachmentsPerRenderer)
                {
                    Renderer renderer = rendererEntry.Key;
                    List<ISkinAttachment> attachments = rendererEntry.Value;
                    attachmentDescs.Clear();
                    meshAttachments.Clear();
                    pointsAttachments.Clear();

                    //fill skin attachment target desc
                    AttachmentTargetData attachmentTargetData = GetAttachmentTargetData(renderer);
                    if (!EnsureRuntimeMeshBuffers(renderer, attachmentTargetData))
                        continue;
                    
                    SkinAttachmentTargetDescCPU attachmentTargetDesc = default;
                    attachmentTargetDesc.positions = attachmentTargetData.meshBuffers.vertexPositions;
                    attachmentTargetDesc.normals = attachmentTargetData.meshBuffers.vertexNormals;
                    attachmentTargetDesc.tangents = attachmentTargetData.meshBuffers.vertexTangents;
                    
                    //separate mesh and point attachments
                    foreach (var skinAttachment in attachments)
                    {
                        if (skinAttachment is ISkinAttachmentMesh meshAttachment)
                        {
                            meshAttachments.Add(meshAttachment);
                        }

                        if (skinAttachment is ISkinAttachmentTransform pointsAttachment)
                        {
                            pointsAttachments.Add(pointsAttachment);
                        }
                    }

                    //gather mesh attachments
                    foreach (var meshAttachment in meshAttachments)
                    {
                        SkinAttachmentDescCPU descCPU = default;
                        if (meshAttachment.FillSkinAttachmentDesc(ref descCPU))
                        {
                            attachmentDescs.Add(descCPU);
                        }
                    }

                    //gather transform Attachments
                    foreach (var pointAttach in pointsAttachments)
                    {
                        SkinAttachmentDescCPU descCPU = default;

                        pointAttach.GetSkinAttachmentPosesAndItem(out descCPU.skinAttachmentPoses, out SkinAttachmentItem item);
                        descCPU.skinAttachmentItems = new SkinAttachmentItem[]{item};
                        descCPU.itemsOffset = 0;
                        descCPU.itemsCount = 1;
                        descCPU.resolveTransform = pointAttach.GetResolveTransform();
                        descCPU.resolvedPositions = pointAttach.GetNormalResolveBufferCPU();
                        descCPU.resolvedNormals = pointAttach.GetPositionResolveBufferCPU();
                        descCPU.resolvedTangents = null;
                        attachmentDescs.Add(descCPU);
                    }

                    if (attachmentDescs.Count > 0)
                    {
                        ResolveSubjectsCPU(ref attachmentTargetDesc, attachmentDescs.ToArray());
                    }

                    //notify attachments about being updated (this potentially will also notify attachments that failed to produce desc, should these be filtered out(?)
                    foreach (var attachment in attachments)
                    {
                        attachment.NotifyAttachmentUpdated(null);
                    }
                }

                queue.Clear();
            }

            private void ResolveAttachmentsGPU(AttachmentResolveQueue queue)
            {
                if (queue.Empty()) return;
                
                KeyValuePair<Renderer, List<ISkinAttachment>>[] attachmentsPerRenderer =
                    queue.GetAttachmentsPerRenderer();

                List<SkinAttachmentDescGPU> attachmentDescs = new List<SkinAttachmentDescGPU>();
                List<ISkinAttachmentMesh> meshAttachments = new List<ISkinAttachmentMesh>();
                List<ISkinAttachmentTransform> pointsAttachments = new List<ISkinAttachmentTransform>();

                CommandBuffer cmd = CommandBufferPool.Get("Resolve SkinAttachments");

                foreach (var rendererEntry in attachmentsPerRenderer)
                {
                    Renderer renderer = rendererEntry.Key;
                    List<ISkinAttachment> attachments = rendererEntry.Value;
                    attachmentDescs.Clear();
                    meshAttachments.Clear();
                    pointsAttachments.Clear();

                    SkinAttachmentTargetDescGPU attachmentTargetDesc = default;

                    if (!FillSkinAttachmentTargetDescFromRenderer(renderer, ref attachmentTargetDesc))
                        continue;

                    //separate mesh and point attachments
                    foreach (var skinAttachment in attachments)
                    {
                        if (skinAttachment is ISkinAttachmentMesh meshAttachment)
                        {
                            meshAttachments.Add(meshAttachment);
                        }

                        if (skinAttachment is ISkinAttachmentTransform pointsAttachment)
                        {
                            pointsAttachments.Add(pointsAttachment);
                        }
                    }

                    //gather mesh attachments
                    foreach (var meshAttachment in meshAttachments)
                    {
                        SkinAttachmentDescGPU descGPU = default;
                        if (meshAttachment.FillSkinAttachmentDesc(ref descGPU))
                        {
                            attachmentDescs.Add(descGPU);
                        }
                    }

                    //gather point attachments
                    foreach (var pointAttach in pointsAttachments)
                    {
                        //TODO
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
                        attachment.NotifyAttachmentUpdated(cmd);
                    }
                }

                Graphics.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                queue.Clear();
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
                        lastBakeDataUpdatedFrame = -1
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