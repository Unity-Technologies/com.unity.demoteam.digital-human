using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;
    
    [PreferBinarySerialization]
    public class SkinAttachmentDataStorage : ScriptableObject
    {
        public Hash128 hashKey;
        public int referenceCount;
        public SkinAttachmentPose[] storedPoses;
        public SkinAttachmentItem[] storedItems;

        public bool LoadAttachmentData(Hash128 hKey, out SkinAttachmentPose[] poses, out SkinAttachmentItem[] items)
        {
            if (hKey == hashKey)
            {
                items = storedItems;
                poses = storedPoses;
                return true;
            }

            poses = null;
            items = null;
            return false;
        }
        
        public void StoreAttachmentData(Hash128 hash, SkinAttachmentPose[] poses, SkinAttachmentItem[] items)
        {
#if UNITY_EDITOR
            hashKey = hash;
            storedItems = items;
            storedPoses = poses;
#endif
        }

        public void AddRef()
        {
            ++referenceCount;
        }
        
        public void Release()
        {
#if UNITY_EDITOR
            --referenceCount;
            if (referenceCount == 0)
            {
                RemoveFile(AssetDatabase.GetAssetPath(this));
            }
#endif
        }

        public void Persist()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            Undo.ClearUndo(this);
#endif
        }
        
        public static string GetStoragePath(Object obj, Hash128 hash)
        {
            if (GetContainingFolderAndParentName(obj, out var dir, out var parentName))
            {
                return Path.Combine(dir, parentName + hash + ".asset");
            }
            return null;
        }

        public static bool GetContainingFolderAndParentName(Object obj, out string directory, out string parentName)
        {
#if UNITY_EDITOR
            var prefabAttachment = PrefabUtility.GetCorrespondingObjectFromOriginalSource(obj);
            if (prefabAttachment != null)
            {
                var prefabPath = AssetDatabase.GetAssetPath(prefabAttachment);
                var directoryPath = Path.GetDirectoryName(prefabPath);
                directory = directoryPath;
                parentName = Path.GetFileNameWithoutExtension(prefabPath);
                directory = Path.Combine(directory, "AttachmentData_" + parentName);
                return true;
            }
            
            {
#if UNITY_2021_2_OR_NEWER
                var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#else
				var prefabStage = Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#endif
                Scene owningScene = new Scene();

                string scenePath = null;
                if (obj is GameObject)
                {
                    var go = (GameObject)obj;
                    owningScene = go.scene;
                }
				
                if (obj is MonoBehaviour)
                {
                    var mb = (MonoBehaviour)obj;
                    owningScene = mb.gameObject.scene;
                }

                if (prefabStage != null && owningScene == prefabStage.scene)
                {
                    scenePath = prefabStage.assetPath;
                }
                else
                {
                    scenePath = owningScene.path;
                }
				
                if (!string.IsNullOrEmpty(scenePath))
                {
                    var directoryPath = Path.GetDirectoryName(scenePath);
                    directory = directoryPath;
                    parentName = Path.GetFileNameWithoutExtension(scenePath);
                    directory = Path.Combine(directory, "AttachmentData_" + parentName);
                    return true;
                }
				
            }
#endif
            directory = null;
            parentName = null;
            return false;
        }
        
        public static SkinAttachmentDataStorage GetOrCreateDefaultDataStorage(MonoBehaviour monoBehaviour, Hash128 hash)
        {
#if UNITY_EDITOR
            var path = GetStoragePath(monoBehaviour, hash);
            if (path == null) return null;
            var directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            SkinAttachmentDataStorage dataEntry = AssetDatabase.LoadAssetAtPath<SkinAttachmentDataStorage>(path);
            if(dataEntry == null)
            {
                dataEntry = CreateInstance<SkinAttachmentDataStorage>();
                AssetDatabase.CreateAsset(dataEntry, path);
                dataEntry = AssetDatabase.LoadAssetAtPath<SkinAttachmentDataStorage>(path);
            }
            
            return dataEntry;
#else
            return null;
#endif
        }
        
        public static SkinAttachmentDataStorage TryGetDefaultDataStorage(MonoBehaviour monoBehaviour, Hash128 bakeHash)
        {
#if UNITY_EDITOR
            var path = GetStoragePath(monoBehaviour, bakeHash);
            if (path == null) return null;
            SkinAttachmentDataStorage dataEntry = AssetDatabase.LoadAssetAtPath<SkinAttachmentDataStorage>(path);
            return dataEntry;
#else
            return null;
#endif
        }
        
        public static bool RemoveFile(string path)
        {
            if (path == null) return false;
#if UNITY_EDITOR
            if (AssetDatabase.AssetPathExists(path))
            {
                AssetDatabase.DeleteAsset(path);
                return true;
            }
#endif
            return false;
        }
        
        public static Hash128 CalculateHash(SkinAttachmentPose[] poses, SkinAttachmentItem[] items)
        {
            ulong checksum0 = 0;
            ulong checksum1 = 0;
            unsafe
            {
                fixed (SkinAttachmentPose* ptrPose = poses)
                fixed (SkinAttachmentItem* ptrItemVer3 = items)
                {
                    ulong* ptrChecksum0 = &checksum0;
                    ulong* ptrChecksum1 = &checksum1;
                    HashUnsafeUtilities.ComputeHash128(ptrPose, (ulong)(sizeof(SkinAttachmentPose) * poses.Length),
                        ptrChecksum0, ptrChecksum1);
                    HashUnsafeUtilities.ComputeHash128(ptrItemVer3, (ulong)(sizeof(SkinAttachmentItem3) * items.Length),
                        ptrChecksum0, ptrChecksum1);

                }
            }
			
            return new Hash128(checksum0, checksum1);
        }
    }
}