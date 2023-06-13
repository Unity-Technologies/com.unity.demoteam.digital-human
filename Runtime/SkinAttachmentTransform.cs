using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;

    [ExecuteAlways]
    public class SkinAttachmentTransform : MonoBehaviour
    {
        public static int TransformAttachmentBufferStride => c_transformAttachmentBufferStride;
        public int CurrentOffsetIntoGPUPositionsBuffer => currentOffsetToTransformGroup;
        public GraphicsBuffer CurrentGPUPositionsBuffer => currentPositionsBufferGPU;
        
        public SkinAttachmentComponentCommon common = new SkinAttachmentComponentCommon();
        public bool IsAttached => common.attached;

        public event Action<CommandBuffer> onSkinAttachmentTransformResolved;
        
        private int currentOffsetToTransformGroup = 0;
        private GraphicsBuffer currentPositionsBufferGPU;
        private const int c_transformAttachmentBufferStride = 3 * sizeof(float);
        
        public void Attach(bool storePositionRotation = true)
        {
            common.Attach( storePositionRotation);
            UpdateAttachedState();
        }

        public void Detach(bool revertPositionRotation = true)
        {
            common.Detach(revertPositionRotation);
        }

        void OnEnable()
        {
            common.Init(transform, BakeAttachmentPoses);
            common.LoadBakedData();
            UpdateAttachedState();
        }
        
        void LateUpdate()
        {
            UpdateAttachedState();
            if (common.hasValidState)
            {
                SkinAttachmentTransformGroupHandler.Inst.AddTransformAttachmentForResolve(this);
            }
        }

        public Renderer GetTargetRenderer()
        {
            return common.currentTarget;
        }

        public void EnsureBakedData()
        {
            common.EnsureBakedData();
        }

        public bool CanAttach()
        {
            return common.IsAttachmentTargetValid() && common.dataStorage != null && !IsAttached;
        }


        void UpdateAttachedState()
        {
            common.UpdateAttachedState();
            
        }
        
        static void BuildDataAttachSubject(in SkinAttachmentPose[] posesArray, in SkinAttachmentItem[] itemsArray, 
            Matrix4x4 resolveMatrix, in MeshInfo targetBakeData, in PoseBuildSettings settings, bool dryRun,
            ref int dryRunPoseCount, ref int dryRunItemCount, ref int itemOffset, ref int poseOffset)
        {
            unsafe
            {
                fixed (int* attachmentIndex = &itemOffset)
                fixed (int* poseIndex = &poseOffset)
                fixed (SkinAttachmentPose* pose = posesArray)
                fixed (SkinAttachmentItem* items = itemsArray)
                {
                    SkinAttachmentDataBuilder.BuildDataAttachTransform(pose, items, resolveMatrix, targetBakeData,
                        settings, dryRun, ref dryRunPoseCount, ref dryRunItemCount, attachmentIndex, poseIndex);
                }
            }
        }

        bool BakeAttachmentPoses(SkinAttachmentComponentCommon.PoseBakeOutput bakeOutput)
        {
            return BakeAttachmentPoses(ref bakeOutput.items, ref bakeOutput.poses);
        }

        bool BakeAttachmentPoses(ref SkinAttachmentItem[] items, ref SkinAttachmentPose[] poses)
        {
            if (!SkinAttachmentSystem.Inst.GetAttachmentTargetMeshInfo(common.attachmentTarget,
                    out MeshInfo attachmentTargetBakeData))
                return false;

            Matrix4x4 subjectToTarget = common.attachmentTarget.transform.worldToLocalMatrix * transform.localToWorldMatrix;

            //for now deactive this path as it's not yet stable
            PoseBuildSettings poseBuildParams = new PoseBuildSettings
            {
                onlyAllowPoseTrianglesContainingAttachedPoint = false
            };

            // pass 1: dry run
            int dryRunPoseCount = 0;
            int dryRunItemCount = 0;

            int currentPoseOffset = 0;
            int currentItemOffset = 0;

            BuildDataAttachSubject(poses, items, subjectToTarget, attachmentTargetBakeData, poseBuildParams, true, ref dryRunPoseCount, ref dryRunItemCount, ref currentItemOffset,
                ref currentPoseOffset);
            
            ArrayUtils.ResizeCheckedIfLessThan(ref poses, dryRunPoseCount);
            ArrayUtils.ResizeCheckedIfLessThan(ref items, dryRunItemCount);

            currentPoseOffset = 0;
            currentItemOffset = 0;
            

            BuildDataAttachSubject(poses, items, subjectToTarget, attachmentTargetBakeData, poseBuildParams, false, ref dryRunPoseCount, ref dryRunItemCount, ref currentItemOffset,
                ref currentPoseOffset);

            return true;
        }

        public void AfterSkinAttachmentGroupResolve(CommandBuffer cmd, Vector3[] positionsCPU, GraphicsBuffer positionsGPU,
            int indexInGroup)
        {
            currentOffsetToTransformGroup = indexInGroup;
            currentPositionsBufferGPU = positionsGPU;
            
            if (common.schedulingMode == SkinAttachmentComponentCommon.SchedulingMode.CPU)
            {
                transform.position = positionsCPU[indexInGroup];
            }

            onSkinAttachmentTransformResolved?.Invoke(cmd);
        }


        public bool GetSkinAttachmentPosesAndItem(out SkinAttachmentPose[] poses, out SkinAttachmentItem item)
        {
            if (common.bakedPoses == null || common.bakedItems == null)
            {
                poses = null;
                item = default;
                return false;
            }
            
            poses = common.bakedPoses;
            item = common.bakedItems[0];
            return true;
        }

    }
}