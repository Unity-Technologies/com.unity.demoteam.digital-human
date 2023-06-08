using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    public interface ISkinAttachment
    {
        public Renderer GetTargetRenderer();

        //called when subject has been updated (if update was on cpu, cmd is null, if on gpu, cmd is commandbuffer that contains the commands to update the mesh on gpu)
        public void NotifyAttachmentUpdated(CommandBuffer cmd);
    }

    public interface ISkinAttachmentMesh : ISkinAttachment
    {
        bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescGPU desc);
        bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescCPU desc);
    };

    public interface ISkinAttachmentPoints : ISkinAttachment
    {
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

        private static ComputeShader s_resolveAttachmentsCS;
        private static int s_resolveAttachmentsPosKernel = 0;
        private static int s_resolveAttachmentsPosNormalKernel = 0;
        private static int s_resolveAttachmentsPosNormalMovecKernel = 0;

        private static bool s_initialized = false;
        private static Instance s_instance;

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

                s_initialized = true;
            }
        }


        public class Instance
        {
            internal class AttachmentTargetData
            {
                public MeshInfo meshInfo;
                public MeshBuffers meshBuffers;
                public int lastTargetUsedFrame;
                public int lastMeshBuffersUpdatedFrame;
                public int lastBakeDataUpdatedFrame;
            }

            private Dictionary<Renderer, AttachmentTargetData> attachmentTargetDict =
                new Dictionary<Renderer, AttachmentTargetData>();

            private AttachmentResolveQueue attachmentResolveQueueGPU = new AttachmentResolveQueue();
            private AttachmentResolveQueue attachmentResolveQueueCPU = new AttachmentResolveQueue();


            public static bool IsValidAttachmentTarget(Renderer r)
            {
                return r is SkinnedMeshRenderer || r is MeshRenderer;
            }
            
            public void ResolveMeshAttachmentExternalGPU(CommandBuffer cmd, SkinAttachmentMesh attachment)
            {
            }

            public void ResolveMeshAttachmentExternalCPU(SkinAttachmentMesh attachment)
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

            public bool GetAttachmentTargetMeshInfo(Renderer r, out MeshInfo info)
            {
                info = new MeshInfo();
                if (IsValidAttachmentTarget(r))
                {
                    AttachmentTargetData data = GetAttachmentTargetData(r);

                    int currentFrame = Time.frameCount;
                    
                    //are meshbuffers up to date?
                    if (data.lastMeshBuffersUpdatedFrame != currentFrame)
                    {
                        Mesh m = GetPoseBakeMesh(r, data);
                        if (m == null) return false;
                        data.meshBuffers.LoadFrom(m);
                        data.lastMeshBuffersUpdatedFrame = currentFrame;
                    }

                    //is bakedata up to date?
                    if (data.lastBakeDataUpdatedFrame != currentFrame)
                    {
                        const bool weldedAdjacency = false;
                        
                        data.meshInfo.meshBuffers = data.meshBuffers;
                        if (data.meshInfo.meshAdjacency == null)
                            data.meshInfo.meshAdjacency = new MeshAdjacency(data.meshBuffers, weldedAdjacency);
                        else 
                            data.meshInfo.meshAdjacency.LoadFrom(data.meshBuffers, weldedAdjacency);
                        
                        if (data.meshInfo.meshVertexBSP == null)
                            data.meshInfo.meshVertexBSP = new KdTree3(data.meshInfo.meshBuffers.vertexPositions, data.meshInfo.meshBuffers.vertexCount);
                        else
                            data.meshInfo.meshVertexBSP.BuildFrom(data.meshInfo.meshBuffers.vertexPositions, data.meshInfo.meshBuffers.vertexCount);

                        data.lastBakeDataUpdatedFrame = currentFrame;
                        
                        info = data.meshInfo;
                    }
                    
                }
                
                return false;
            }

            Mesh GetPoseBakeMesh(Renderer r, AttachmentTargetData data)
            {
                
            }
            
            void ResolveAttachmentsCPU()
            {
                KeyValuePair<Renderer, List<ISkinAttachment>>[] attachmentsPerRenderer =
                    attachmentResolveQueueGPU.GetAttachmentsPerRenderer();

                List<SkinAttachmentDescCPU> attachmentDescs = new List<SkinAttachmentDescCPU>();
                List<ISkinAttachmentMesh> meshAttachments = new List<ISkinAttachmentMesh>();
                List<ISkinAttachmentPoints> pointsAttachments = new List<ISkinAttachmentPoints>();

                foreach (var rendererEntry in attachmentsPerRenderer)
                {
                    Renderer renderer = rendererEntry.Key;
                    List<ISkinAttachment> attachments = rendererEntry.Value;
                    attachmentDescs.Clear();
                    meshAttachments.Clear();
                    pointsAttachments.Clear();

                    SkinAttachmentTargetDescCPU attachmentTargetDesc = default;

                    //fill skin attachment target desc

                    //separate mesh and point attachments
                    foreach (var skinAttachment in attachments)
                    {
                        if (skinAttachment is ISkinAttachmentMesh meshAttachment)
                        {
                            meshAttachments.Add(meshAttachment);
                        }

                        if (skinAttachment is ISkinAttachmentPoints pointsAttachment)
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
                        //TODO
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

                attachmentResolveQueueCPU.Clear();
            }

            private void ResolveAttachmentGPU()
            {
                KeyValuePair<Renderer, List<ISkinAttachment>>[] attachmentsPerRenderer =
                    attachmentResolveQueueGPU.GetAttachmentsPerRenderer();

                List<SkinAttachmentDescGPU> attachmentDescs = new List<SkinAttachmentDescGPU>();
                List<ISkinAttachmentMesh> meshAttachments = new List<ISkinAttachmentMesh>();
                List<ISkinAttachmentPoints> pointsAttachments = new List<ISkinAttachmentPoints>();

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

                        if (skinAttachment is ISkinAttachmentPoints pointsAttachment)
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

                attachmentResolveQueueGPU.Clear();
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