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
            ReferencePoses
        }
        
        public enum SchedulingMode
        {
            CPU,
            GPU
        }

        public class PoseBakeOutput
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
        public SchedulingMode schedulingMode = SchedulingMode.GPU;
        public PoseDataSource poseDataSource = PoseDataSource.BuildPoses;
        [VisibleIfAttribute("poseDataSource", PoseDataSource.ReferencePoses)]
        public SkinAttachmentDataStorage referencePoseDataStorage;
        public bool explicitScheduling = false;
        public Mesh explicitBakeMesh = null;
        public bool readbackTargetMeshWhenBaking = true;

        public bool IsAttached => attached;
        public Hash128 CheckSum => checkSum;

        public bool showAttachmentTargetForBaking = false;
        
        [SerializeField] [HideInInspector] internal bool attached = false;
        [SerializeField] [HideInInspector] internal Vector3 attachedLocalPosition;
        [SerializeField] [HideInInspector] internal Quaternion attachedLocalRotation;
        [SerializeField] [HideInInspector] internal Hash128 checkSum;
        [SerializeField] [HideInInspector] internal PoseDataSource currentPoseDataSource;
        [SerializeField] [HideInInspector] public SkinAttachmentDataStorage currentAttachmentDataStorage;
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
            if (currentAttachmentDataStorage)
            {
                currentAttachmentDataStorage.Release();
                currentAttachmentDataStorage = null;
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
                
                UpdateBakedData(attachment, allowBakeRefresh);
                EnsureBakedDataStorageIsValid(attachment);
                EnsureBakedDataIsLoaded(attachment);
                hasValidState = currentTarget != null && ValidateBakedData();
            }
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
            else if (currentPoseDataSource == PoseDataSource.ReferencePoses)
            {
                if (currentAttachmentDataStorage == null)
                {
                    if (referencePoseDataStorage != null)
                    {
                        checkSum = referencePoseDataStorage.hashKey;
                        currentAttachmentDataStorage = referencePoseDataStorage;
                        currentTarget = attachmentTarget;
                        LoadBakedData();
                        currentAttachmentDataStorage.AddRef();
                    }
                    
                }
            }
        }

        internal void StoreBakedData(MonoBehaviour attachment, SkinAttachmentItem[] items, SkinAttachmentPose[] poses)
        {
            Hash128 newHash = SkinAttachmentDataStorage.CalculateHash(poses, items);
            
            if (newHash == checkSum && checkSum.isValid && currentAttachmentDataStorage != null && currentAttachmentDataStorage.hashKey == newHash) return;
            
            var storage = SkinAttachmentDataStorage.GetOrCreateDefaultDataStorage(attachment, newHash);
            if (storage != currentAttachmentDataStorage && currentAttachmentDataStorage != null)
            {
                currentAttachmentDataStorage.Release();
            }

            currentAttachmentDataStorage = storage;

            if (currentAttachmentDataStorage != null)
            {
                storage.StoreAttachmentData(newHash, poses, items);
                storage.Persist();
            }
            
            checkSum = newHash;
            
#if UNITY_EDITOR
            EditorUtility.SetDirty(attachment);
            Undo.ClearUndo(attachment);
#endif
            LoadBakedData();
        }

        internal void TryToFindAttachmentStorage(MonoBehaviour attachment)
        {
            if (!checkSum.isValid) return;
            currentAttachmentDataStorage = SkinAttachmentDataStorage.TryGetDefaultDataStorage(attachment, checkSum);
        }
        internal void LoadBakedData()
        {
            if (checkSum.isValid && currentAttachmentDataStorage != null)
            {
                currentAttachmentDataStorage.LoadAttachmentData(checkSum, out bakedPoses, out bakedItems);
            }
        }
        
        internal void EnsureBakedDataStorageIsValid(MonoBehaviour attachment)
        {
            if (currentAttachmentDataStorage == null)
            {
                TryToFindAttachmentStorage(attachment);
            }
        }
        
        internal void EnsureBakedDataIsLoaded(MonoBehaviour attachment)
        {
            if (bakedPoses == null || bakedItems == null)
            {
                LoadBakedData();
            }
        }


        internal bool ValidateBakedData()
        {
            bool dataExists = currentAttachmentDataStorage != null && bakedPoses != null && bakedItems != null && bakedPoses.Length > 0 && bakedItems.Length > 0;
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