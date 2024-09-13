using System;
using System.Collections;
using System.Collections.Generic;
using Unity.DemoTeam.DigitalHuman;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;

    
    
    [Serializable]
    public class SkinAttachmentComponentCommon
    {
        public enum PoseDataSource
        {
            BuildPoses,
            LinkPosesByChecksum,
        }
        
        public enum SchedulingMode
        {
            CPU,
            GPU
        }

        public  class PoseBakeOutput
        { 
            public SkinAttachmentPose[] poses;
            public SkinAttachmentItem[] items;
        }
        
        public interface ISkinAttachmentComponent
        {
            SkinAttachmentComponentCommon GetCommonComponent();
            bool BakeAttachmentData(PoseBakeOutput output);
            void RevertPropertyOverrides();
        }
        
        internal struct BakeData
        {
            public MeshBuffers meshBuffers;
            public MeshAdjacency meshAdjacency;
            public MeshIslands meshIslands;
        }

        public Renderer attachmentTarget;
        public SkinAttachmentDataRegistry dataStorage;
        public SchedulingMode schedulingMode = SchedulingMode.GPU;
        public PoseDataSource poseDataSource = PoseDataSource.BuildPoses;
        public Hash128 linkedChecksum;
        public bool explicitScheduling = false;
        public Mesh explicitBakeMesh = null;
        public bool readbackTargetMeshWhenBaking = true;
        public string bakedDataEntryName;

        public bool IsAttached => attached;
        public Hash128 CheckSum => checkSum;

        public bool showAttachmentTargetForBaking = false;
        
        [SerializeField] [HideInInspector] internal bool attached = false;
        [SerializeField] [HideInInspector] internal Vector3 attachedLocalPosition;
        [SerializeField] [HideInInspector] internal Quaternion attachedLocalRotation;
        [SerializeField] [HideInInspector] internal Hash128 checkSum;
        [SerializeField] [HideInInspector] internal PoseDataSource currentPoseDataSource;
        [SerializeField] [HideInInspector] internal SkinAttachmentDataRegistry currentStorage;
        [SerializeField] [HideInInspector] internal Renderer currentTarget;
        
        internal SkinAttachmentPose[] bakedPoses;
        internal SkinAttachmentItem[] bakedItems;

        internal bool hasValidState = false;

        internal void Attach(MonoBehaviour attachment, bool storePositionRotation = true)
        {
            if (storePositionRotation)
            {
                attachedLocalPosition = attachment.transform.localPosition;
                attachedLocalRotation = attachment.transform.localRotation;
            }

            attached = true;

            UpdateAttachedState(attachment, true);
        }

        internal void Detach(MonoBehaviour attachment, bool revertPositionRotation = true)
        {
            if (revertPositionRotation)
            {
                attachment.transform.localPosition = attachedLocalPosition;
                attachment.transform.localRotation = attachedLocalRotation;
            }

            attached = false;
            currentTarget = null;
            if (dataStorage)
            {
                dataStorage.ReleaseAttachmentData(checkSum);
                checkSum = default;
            }
        }

        internal bool IsAttachmentTargetValid()
        {
            return SkinAttachmentSystem.IsValidAttachmentTarget(attachmentTarget);
        }

        internal void UpdateAttachedState(MonoBehaviour attachment, bool allowBakeRefresh = false)
        {
            hasValidState = false;
            if (attachmentTarget == null) return;

            //make sure target is a supported renderer (meshrenderer or skinnedMeshRenderer)
            if (!(attachmentTarget is SkinnedMeshRenderer || attachmentTarget is MeshRenderer))
            {
                attachmentTarget = null;
                currentTarget = null;
                Detach(attachment);
                return;
            }

            //target changed, detach
            if (currentTarget != attachmentTarget && currentTarget != null)
            {
                Detach(attachment);
                return;
            }

            if (attached)
            {
                if (!IsAttachmentTargetValid())
                {
                    hasValidState = false;
                    return;
                }
                
                ValidateDataStorage(attachment, allowBakeRefresh);

                if (currentStorage != null)
                {
                    UpdateBakedData(attachment, allowBakeRefresh);
                }

                EnsureBakedDataIsLoaded();
                
                hasValidState = currentTarget != null && ValidateBakedData();
            }
        }

        public bool HasDataStorageChanged()
        {
            return currentStorage != dataStorage;
        }
        
        public bool BakeAttachmentDataToSceneOrPrefab(MonoBehaviour attachment)
        {
            bool succesfull = true;
#if UNITY_EDITOR
            succesfull = BakeAttachmentData(attachment);
#endif
            return succesfull;
        }
        
        private bool BakeAttachmentData(MonoBehaviour attachment)
        {
            bool bakeSuccessfull = BakeAndStoreData(attachment);
            if (bakeSuccessfull)
            {
                currentTarget = attachmentTarget;
            }

            return bakeSuccessfull;
        }

        private bool BakeAttachmentDataToPrefab(MonoBehaviour attachment)
        {
#if UNITY_EDITOR		
            var prefabAttachment = PrefabUtility.GetCorrespondingObjectFromOriginalSource(attachment);
            var prefabPath = AssetDatabase.GetAssetPath(prefabAttachment);
            
#if UNITY_2021_2_OR_NEWER
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#else
            var prefabStage = Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#endif
            GameObject prefabContainer = null;
            
            if (prefabStage == null || prefabStage.assetPath != prefabPath)
            {
                Debug.LogFormat(attachment, "{0}: rebaking attachment data for prefab '{1}'...", attachment.name, prefabPath);
                prefabContainer = PrefabUtility.LoadPrefabContents(prefabPath);
            }

            ISkinAttachmentComponent attachmentComponentInterfacePrefab =
                prefabAttachment as ISkinAttachmentComponent;
            
            if(attachmentComponentInterfacePrefab != null)
            {
                attachmentComponentInterfacePrefab.GetCommonComponent().BakeAttachmentData(prefabAttachment);
            }
            

            if (prefabContainer != null)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabContainer, prefabPath);
                PrefabUtility.UnloadPrefabContents(prefabContainer);
            }
    
            //revert the instances overrides
            ISkinAttachmentComponent attachmentComponentInterfaceInstance =
                attachment as ISkinAttachmentComponent;
            attachmentComponentInterfaceInstance?.RevertPropertyOverrides();
#endif
            return true;
        }

		private bool BakeAndStoreData(MonoBehaviour attachment)
		{
			PoseBakeOutput bakeOutput = new PoseBakeOutput();
			bakeOutput.items = default;
            bakeOutput.poses = default;
            ISkinAttachmentComponent attachmentComponent = attachment as ISkinAttachmentComponent;
            bool bakeSuccessfull = attachmentComponent?.BakeAttachmentData(bakeOutput) ?? false;
			if (bakeSuccessfull)
            {
                StoreBakedData(attachment, bakeOutput.items, bakeOutput.poses);
            }
			return bakeSuccessfull;
		}

        internal void UpdateBakedData(MonoBehaviour attachment, bool allowBakeRefresh)
        {
            if (currentPoseDataSource == PoseDataSource.BuildPoses)
            {
                if (allowBakeRefresh)
                {
                    bool needRebake = currentTarget == null;
                    if (needRebake)
                    {
                        BakeAttachmentDataToSceneOrPrefab(attachment);
                    }
                }
            } 
            else if (currentPoseDataSource == PoseDataSource.LinkPosesByChecksum)
            {
                if (linkedChecksum != checkSum || currentTarget == null)
                {
                    if (linkedChecksum.isValid)
                    {
                        checkSum = linkedChecksum;
                        currentTarget = attachmentTarget;
                        LoadBakedData();
                        currentStorage.UseAttachmentData(checkSum);
                    }
                    
                }
            }
        }

        internal void ValidateDataStorage(MonoBehaviour attachment, bool allowUsingDefault)
        {
            
            if (currentStorage != null && currentStorage != dataStorage || currentPoseDataSource != poseDataSource)
            {
                Detach(attachment);
                checkSum = default;
                currentStorage = null;
            }

            if (allowUsingDefault && dataStorage == null)
            {
                dataStorage = SkinAttachmentDataRegistry.GetOrCreateDefaultSkinAttachmentRegistry(attachment);
            }

            currentPoseDataSource = poseDataSource;
            currentStorage = dataStorage;
        }

        internal void StoreBakedData(MonoBehaviour attachment, SkinAttachmentItem[] items, SkinAttachmentPose[] poses)
        {
            if (currentStorage != null)
            {

                string name = bakedDataEntryName;
                if (name == null)
                {
                    name = attachment != null ? attachment.name : "<unnamed>";
                }

                Hash128 newHash = SkinAttachmentDataRegistry.CalculateHash(poses, items);

                if (newHash == checkSum && checkSum.isValid) return;
                
                if (checkSum.isValid)
                {
                    currentStorage.ReleaseAttachmentData(checkSum);
                }

                checkSum = currentStorage.UseAttachmentData(poses, items);
                

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
        
        internal void EnsureBakedDataIsLoaded()
        {
            if (bakedPoses == null || bakedItems == null)
            {
                LoadBakedData();
            }
        }


        internal bool ValidateBakedData()
        {
            bool dataExists = currentStorage != null && bakedPoses != null && bakedItems != null && bakedPoses.Length > 0 && bakedItems.Length > 0;
            return dataExists;
        }
#if UNITY_EDITOR
        internal void DrawDebug(MonoBehaviour attachment)
        {
            if (showAttachmentTargetForBaking && IsAttachmentTargetValid())
            {
                var prevMatrix = Gizmos.matrix;
                var prevColor = Gizmos.color;
                
                Gizmos.matrix = attachmentTarget.transform.localToWorldMatrix;
                var color = Color.yellow;
                color.a = 0.5f;
                Gizmos.color = color;
                
                Mesh m = SkinAttachmentSystem.Inst.GetPoseBakeMesh(attachmentTarget, explicitBakeMesh, readbackTargetMeshWhenBaking);
                Gizmos.DrawMesh(m);
                
                Gizmos.matrix = prevMatrix;
                Gizmos.color = prevColor;
            }
        }
#endif
    }
}