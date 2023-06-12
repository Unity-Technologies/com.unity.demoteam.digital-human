using System;
using System.Collections;
using System.Collections.Generic;
using Unity.DemoTeam.DigitalHuman;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;

    [Serializable]
    public class SkinAttachmentComponentCommon
    {
        public enum SchedulingMode
        {
            CPU,
            GPU
        }
        
        internal struct BakeData
        {
            public MeshBuffers meshBuffers;
            public MeshAdjacency meshAdjacency;
            public MeshIslands meshIslands;
        }

        internal class PoseBakeOutput
        { 
            public SkinAttachmentPose[] poses;
            public SkinAttachmentItem[] items;
        }

        public Renderer attachmentTarget;
        public SkinAttachmentDataStorage dataStorage;
        public SchedulingMode schedulingMode;
        

        public bool IsAttached => attached;

        [SerializeField] [HideInInspector] internal bool attached = false;
        [SerializeField] [HideInInspector] internal Vector3 attachedLocalPosition;
        [SerializeField] [HideInInspector] internal Quaternion attachedLocalRotation;
        [SerializeField] [HideInInspector] internal Hash128 checkSum;
        [SerializeField] [HideInInspector] internal SkinAttachmentDataStorage currentStorage;

        internal SkinAttachmentPose[] bakedPoses;
        internal SkinAttachmentItem[] bakedItems;

        internal Renderer currentTarget;
        
        internal bool hasValidState = false;
        private Transform transform;
        private Func<PoseBakeOutput, bool> bakeAttachmentsFunc;

        internal void Init(Transform t, Func<PoseBakeOutput, bool> bakeAttachmentPosesFunc)
        {
            transform = t;
            bakeAttachmentsFunc = bakeAttachmentPosesFunc;
        }

        internal void Attach(bool storePositionRotation = true)
        {
            if (storePositionRotation)
            {
                attachedLocalPosition = transform.localPosition;
                attachedLocalRotation = transform.localRotation;
            }

            attached = true;
        }

        internal void Detach(bool revertPositionRotation = true)
        {
            if (revertPositionRotation)
            {
                transform.localPosition = attachedLocalPosition;
                transform.localRotation = attachedLocalRotation;
            }

            attached = false;
            currentTarget = null;
        }

        internal bool IsAttachmentTargetValid()
        {
            return SkinAttachmentSystem.IsValidAttachmentTarget(attachmentTarget);
        }

        internal void UpdateAttachedState()
        {
            hasValidState = false;
            if (attachmentTarget == null) return;

            //make sure target is a supported renderer (meshrenderer or skinnedMeshRenderer)
            if (!(attachmentTarget is SkinnedMeshRenderer || attachmentTarget is MeshRenderer))
            {
                attachmentTarget = null;
                currentTarget = null;
                Detach();
                return;
            }

            //target changed, detach
            if (currentTarget != attachmentTarget && currentTarget != null)
            {
                Detach();
                return;
            }

            if (attached)
            {
                if (!IsAttachmentTargetValid())
                {
                    hasValidState = false;
                    return;
                }


                ValidateDataStorage();
                EnsureBakedData();

                hasValidState = currentTarget != null && ValidateBakedData();
            }
        }

        public void EnsureBakedData()
        {
            bool newDataBaked = false;

            PoseBakeOutput bakeOutput = new PoseBakeOutput();
            
            bakeOutput.items = default;
            bakeOutput.poses = default;
            
            if (currentTarget == null)
            {
                bool bakeSuccessfull = bakeAttachmentsFunc(bakeOutput);
                if (bakeSuccessfull)
                {
                    currentTarget = attachmentTarget;
                    newDataBaked = true;
                }
            }
            else if (!ValidateBakedData())
            {
                bool bakeSuccessfull = bakeAttachmentsFunc(bakeOutput);
                if (bakeSuccessfull)
                {
                    newDataBaked = true;
                }
            }

            if (newDataBaked)
            {
                StoreBakedData(bakeOutput.items, bakeOutput.poses);
            }
        }

        internal void ValidateDataStorage()
        {
            if (currentStorage != null && currentStorage != dataStorage)
            {
                if (checkSum.isValid)
                {
                    currentStorage.RemoveAttachmentData(checkSum);
                    checkSum = default;
                }

                currentStorage = null;
            }

            currentStorage = dataStorage;
        }

        internal void StoreBakedData(SkinAttachmentItem[] items, SkinAttachmentPose[] poses)
        {
            if (currentStorage != null)
            {
                if (checkSum.isValid)
                {
                    checkSum = currentStorage.UpdateAttachmentData(poses, items, checkSum);
                }
                else
                {
                    checkSum = currentStorage.StoreAttachmentData(poses, items);
                }

                LoadBakedData();
            }
        }

        internal void LoadBakedData()
        {
            if (checkSum.isValid && currentStorage != null)
            {
                currentStorage.LoadAttachmentData(checkSum, out bakedPoses, out bakedItems);
            }
        }


        internal bool ValidateBakedData()
        {
            bool dataExists = currentStorage != null && bakedPoses != null && bakedItems != null;
            return dataExists;
        }
    }
}