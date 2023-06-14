using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.DemoTeam.DigitalHuman;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Used to implement the ISkinAttachment interface. The reason for this (instead of SkinAttachmentTransform directly implementing it)
 * is to be able to batch more skin attachments to be resolved in one go. Resolving one by one on GPU would lead to dispatch per transform attachment,
 * which doesn't really scale very well
 */
namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;

    public static class SkinAttachmentTransformGroupHandler
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

        private static Instance s_instance;

        public class Instance
        {
            private Dictionary<Renderer, SkinAttachmentTransformGroup> cpuGroups = new Dictionary<Renderer, SkinAttachmentTransformGroup>();
            private Dictionary<Renderer, SkinAttachmentTransformGroup> gpuGroups = new Dictionary<Renderer, SkinAttachmentTransformGroup>();

            public void AddTransformAttachmentForResolve(SkinAttachmentTransform attachment)
            {
                Renderer rend = attachment.GetTargetRenderer();
                
                switch (attachment.common.schedulingMode)
                {
                    case SkinAttachmentComponentCommon.SchedulingMode.CPU:
                        AddAttachment(attachment, cpuGroups);
                        break;
                    case SkinAttachmentComponentCommon.SchedulingMode.GPU:
                        AddAttachment(attachment, gpuGroups);
                        break;
                }
                
            }

            private static void AddAttachment(SkinAttachmentTransform attachment,
                Dictionary<Renderer, SkinAttachmentTransformGroup> destination)
            {
                Renderer rend = attachment.GetTargetRenderer();
                if (destination.TryGetValue(rend, out SkinAttachmentTransformGroup group))
                {
                    group.AddAttachment(attachment);
                }
                else
                {
                    SkinAttachmentTransformGroup newGroup = new SkinAttachmentTransformGroup();
                    destination.Add(rend, newGroup);
                    newGroup.AddAttachment(attachment);
                }
            }
            
        }
    }

    public class SkinAttachmentTransformGroup : ISkinAttachment
    {
        private List<SkinAttachmentTransform> attachments = new List<SkinAttachmentTransform>();
        private Renderer targetRenderer;
        private SkinAttachmentComponentCommon.SchedulingMode schedulingMode;
        private GraphicsBuffer combinedItemsGPU;
        private GraphicsBuffer combinedPosesGPU;
        private GraphicsBuffer outputPositionsGPU;

        private SkinAttachmentItem[] combinedItems;
        private SkinAttachmentPose[] combinedPoses;
        private Vector3[] outputPositions;
        private Vector3[] outputNormals;

        public void AddAttachment(SkinAttachmentTransform attachment)
        {
            if (attachments.Count == 0)
            {
                targetRenderer = attachment.GetTargetRenderer();
                schedulingMode = attachment.common.schedulingMode;
                attachments.Add(attachment);
                SkinAttachmentSystem.Inst.QueueAttachmentResolve(this, schedulingMode == SkinAttachmentComponentCommon.SchedulingMode.GPU);
            }
            else
            {
                if (targetRenderer == attachment.GetTargetRenderer() && schedulingMode == attachment.common.schedulingMode)
                {
                    attachments.Add(attachment);
                }
                else
                {
                    if (targetRenderer != attachment.GetTargetRenderer())
                    {
                        Debug.LogErrorFormat("Tried to group attachments with different target, ignoring attachment {0}",
                            attachment.name);
                    }
                    
                    if (schedulingMode != attachment.common.schedulingMode)
                    {
                        Debug.LogErrorFormat("Tried to group attachments with different scheduling modes, ignoring attachment {0}",
                            attachment.name);
                    }
                    
                }
            }
        }

        void CombineBakedData()
        {
            //calculate combined items and & poses
            int combinedPoseCount = 0;
            int combinedItemCount = 0;
            for (int i = 0; i < attachments.Count; ++i)
            {
                if (attachments[i]
                    .GetSkinAttachmentPosesAndItem(out SkinAttachmentPose[] poses, out SkinAttachmentItem item))
                {
                    ++combinedItemCount;
                    combinedPoseCount += poses.Length;
                }
            }

            if (combinedPoseCount == 0 || combinedItemCount == 0)
            {
                combinedItems = null;
                combinedPoses = null;
            }

            ArrayUtils.ResizeChecked(ref combinedItems, combinedItemCount);
            ArrayUtils.ResizeChecked(ref combinedPoses, combinedPoseCount);

            //combine items and poses
            int itemOffset = 0;
            int poseOffset = 0;
            for (int i = 0; i < attachments.Count; ++i)
            {
                if (attachments[i].GetSkinAttachmentPosesAndItem(out SkinAttachmentPose[] poses, out SkinAttachmentItem item))
                {
                    item.poseIndex += poseOffset; //bump the poseindex since we are combining the data entries
                    combinedItems[itemOffset++] = item;
                    for (int k = 0; k < poses.Length; ++k)
                    {
                        combinedPoses[poseOffset++] = poses[k];
                    }
                }
            }
        }


        static void EnsureGraphicsBuffer(ref GraphicsBuffer buf, GraphicsBuffer.Target t, int count, int stride)
        {
            if (buf != null && buf.count < count)
            {
                buf.Release();
                buf = null;
            }

            if (buf != null && !buf.IsValid())
            {
                buf = null;
            }

            if (buf == null)
            {
                buf = new GraphicsBuffer(t, count, stride);
            }
        }


        void PrepareGPUResources()
        {
            SkinAttachmentSystem.UploadAttachmentPoseDataToGPU(combinedItems, combinedPoses, 0, combinedItems.Length, 0,
                combinedPoses.Length, ref combinedItemsGPU, ref combinedPosesGPU);
            EnsureGraphicsBuffer(ref outputPositionsGPU, GraphicsBuffer.Target.Raw, attachments.Count,
                SkinAttachmentTransform.TransformAttachmentBufferStride);
        }

        
        void CreateCPUResources()
        {

            ArrayUtils.ResizeChecked(ref outputPositions, attachments.Count);
            ArrayUtils.ResizeChecked(ref outputNormals, attachments.Count);
        }

        public void ReleaseGPUResources()
        {
            combinedItemsGPU?.Release();
            combinedItemsGPU = null;
            
            combinedPosesGPU?.Release();
            combinedPosesGPU = null;
            
            outputPositionsGPU?.Release();
            outputPositionsGPU = null;
        }

        void ClearAttachments()
        {
            attachments.Clear();
        }

        Matrix4x4 GetResolveTransform()
        {
            Matrix4x4 targetToWorld;
            {
                if (targetRenderer is SkinnedMeshRenderer)
                    targetToWorld = targetRenderer.transform.parent.localToWorldMatrix * Matrix4x4.TRS(targetRenderer.transform.localPosition,
                        targetRenderer.transform.localRotation, Vector3.one);
                else
                    targetToWorld = targetRenderer.localToWorldMatrix;
            }

            return targetToWorld;
        }
        
        //ISkinAttachment
        public Renderer GetTargetRenderer()
        {
            return targetRenderer;
        }

        public void NotifyAttachmentResolved(CommandBuffer cmd)
        {
            int offset = 0;
            foreach (var att in attachments)
            {
                att.AfterSkinAttachmentGroupResolve(cmd, outputPositions, outputPositionsGPU, offset++);
            }
        }
        
        public void NotifyAllAttachmentsFromQueueResolved()
        {
            foreach (var att in attachments)
            {
                att.AfterAllAttachmentsInQueueResolved();
            }
            
            //were done, clear attachments (keep buffers alive for reuse)
            ClearAttachments();
        }

        public bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescGPU desc)
        {
            CombineBakedData();
            PrepareGPUResources();
            desc.itemsBuffer = combinedItemsGPU;
            desc.posesBuffer = combinedPosesGPU;
            desc.itemsCount = combinedItems.Length;
            desc.itemsOffset = 0;
            desc.targetToAttachment = GetResolveTransform();
            desc.positionsNormalsTangentsBuffer = outputPositionsGPU;
            desc.positionsNormalsTangentsOffsetStride = (0, -1, -1, SkinAttachmentTransform.TransformAttachmentBufferStride);
            desc.resolveNormalsAndTangents = false;
            desc.movecsBuffer = null;
            desc.movecsOffsetStride = default;
            desc.releaseOutputBuffersAfterResolve = false;
            return true;
        }

        public bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescCPU desc)
        {
            CombineBakedData();
            CreateCPUResources();
            desc.resolveTransform = GetResolveTransform();
            desc.itemsOffset = 0;
            desc.itemsCount = combinedItems.Length;
            desc.skinAttachmentItems = combinedItems;
            desc.skinAttachmentPoses = combinedPoses;
            desc.resolvedPositions = outputPositions;
            desc.resolvedNormals = outputNormals;
            desc.resolvedTangents = null;
            return true;
        }
    }
}