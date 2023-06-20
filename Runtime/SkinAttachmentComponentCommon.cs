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
        public bool explicitScheduling = false;
        public bool allowAutomaticRebake = false;        

        public bool IsAttached => attached;
        public Hash128 CheckSum => checkSum;
        
        [SerializeField] [HideInInspector] internal bool attached = false;
        [SerializeField] [HideInInspector] internal Vector3 attachedLocalPosition;
        [SerializeField] [HideInInspector] internal Quaternion attachedLocalRotation;
        [SerializeField] [HideInInspector] internal Hash128 checkSum;
        [SerializeField] [HideInInspector] internal SkinAttachmentDataStorage currentStorage;
        [SerializeField] [HideInInspector] internal Renderer currentTarget;
        
        internal SkinAttachmentPose[] bakedPoses;
        internal SkinAttachmentItem[] bakedItems;

        
        
        internal bool hasValidState = false;
        private MonoBehaviour attachment;
        private Func<PoseBakeOutput, bool> bakeAttachmentsFunc;

        internal void Init(MonoBehaviour att, Func<PoseBakeOutput, bool> bakeAttachmentPosesFunc)
        {
            attachment = att;
            bakeAttachmentsFunc = bakeAttachmentPosesFunc;
        }

        internal void Attach(bool storePositionRotation = true)
        {
            if (storePositionRotation)
            {
                attachedLocalPosition = attachment.transform.localPosition;
                attachedLocalRotation = attachment.transform.localRotation;
            }

            attached = true;
        }

        internal void Detach(bool revertPositionRotation = true)
        {
            if (revertPositionRotation)
            {
                attachment.transform.localPosition = attachedLocalPosition;
                attachment.transform.localRotation = attachedLocalRotation;
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
                if (currentStorage != null)
                {
                    EnsureBakedData();
                }
                

                hasValidState = currentTarget != null && ValidateBakedData();
            }
        }

        public void EnsureBakedData(bool forceRebake = false)
        {
            if (allowAutomaticRebake || forceRebake)
            {
                if (currentTarget == null || forceRebake)
                {
                    bool bakeSuccessfull = BakeAndStoreData();
                    if (bakeSuccessfull)
                    {
                        currentTarget = attachmentTarget;
                    }
                }
                else if (!ValidateBakedData())
                {
                    bool bakeSuccessfull = BakeAndStoreData();
                }
            }
            
        }

		private bool BakeAndStoreData()
		{
			PoseBakeOutput bakeOutput = new PoseBakeOutput();
			bakeOutput.items = default;
            bakeOutput.poses = default;
            bool bakeSuccessfull = bakeAttachmentsFunc(bakeOutput);
			if (bakeSuccessfull)
            {
                StoreBakedData( bakeOutput.items, bakeOutput.poses);
            }
			return bakeSuccessfull;
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
                    checkSum = currentStorage.UpdateAttachmentData(attachment.name, poses, items, checkSum);
                }
                else
                {
                    checkSum = currentStorage.StoreAttachmentData(attachment.name, poses, items);
                }

#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(attachment);
                UnityEditor.Undo.ClearUndo(attachment);
#endif
                
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