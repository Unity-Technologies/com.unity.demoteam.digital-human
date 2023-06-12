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
    using BakeData = SkinAttachmentComponentCommon.BakeData;
    [ExecuteAlways]
    public class SkinAttachmentTransform : MonoBehaviour, ISkinAttachmentTransform
    {
        public SkinAttachmentComponentCommon common;
        public bool IsAttached => common.attached;

        private Vector3[] normalResolveBuffer = new Vector3[1];
        private Vector3[] positionResolveBuffer = new Vector3[1];
        
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

        private void OnDisable()
        {
            
        }

        void LateUpdate()
        {
            UpdateAttachedState();
            if (common.hasValidState)
            {
                SkinAttachmentSystem.Inst.QueueAttachmentResolve(this, common.schedulingMode == SkinAttachmentComponentCommon.SchedulingMode.GPU);
            }
        }

        public Renderer GetTargetRenderer()
        {
            return common.currentTarget;
        }
        
        public bool ValidateBakedData()
        {
            bool validData = common.ValidateBakedData();
            return validData;
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
            

            BuildDataAttachSubject(poses, items, subjectToTarget, attachmentTargetBakeData, poseBuildParams, true, ref dryRunPoseCount, ref dryRunItemCount, ref currentItemOffset,
                ref currentPoseOffset);

            return true;
        }

        public void NotifyAttachmentUpdated(CommandBuffer cmd)
        {
            if (common.schedulingMode == SkinAttachmentComponentCommon.SchedulingMode.CPU)
            {
                transform.position = positionResolveBuffer[0];
                transform.localRotation = Quaternion.FromToRotation(Vector3.up, normalResolveBuffer[0]);
            }
            else
            {
                //TODO
            }
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

        public Matrix4x4 GetResolveTransform()
        {
            Matrix4x4 targetToWorld;
            {
                if (common.currentTarget is SkinnedMeshRenderer)
                    targetToWorld = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                else
                    targetToWorld = transform.localToWorldMatrix;
            }

            return targetToWorld;
        }

        public Vector3[] GetPositionResolveBufferCPU()
        {
            return positionResolveBuffer;
        }

        public Vector3[] GetNormalResolveBufferCPU()
        {
            return normalResolveBuffer;
        }
    }
}