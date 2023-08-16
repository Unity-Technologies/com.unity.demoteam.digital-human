using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.DemoTeam.DigitalHuman
{
	using SkinAttachmentItem = SkinAttachmentItem3;
	
	[CreateAssetMenu(menuName = "Digital Human/Skin Attachment Data Registry")]
	[PreferBinarySerialization]
	public class SkinAttachmentDataRegistry : ScriptableObject
	{

		[Serializable]
		public class DataStorageHeader
		{
			public SkinAttachmentDataEntry reference;  //Todo: make skin attachment data to be weak reference (Addressables?) and only load on demand
			public string timeStamp;
			public Hash128 hashKey;
			public int referenceCount;
			public int itemCount;
			public int poseCount;

		}

		[SerializeField][HideInInspector]
		private DataStorageHeader[] databaseEntries ;

		[NonSerialized] private Dictionary<Hash128, DataStorageHeader> dataStorageLookup;

		public DataStorageHeader[] GetAllEntries()
		{
			DataStorageHeader[] copy = null;
			if (databaseEntries == null) return copy;
			ArrayUtils.CopyChecked(databaseEntries, ref copy, databaseEntries.Length);
			return copy;
		}
		
		public Hash128 UseAttachmentData(SkinAttachmentPose[] poses, SkinAttachmentItem[] items)
		{
#if UNITY_EDITOR
			Hash128 h = StoreAttachmentDataInternal(poses, items);
			Persist();
			return h;
#else
			return default;
#endif
		}
		
		public void ReleaseAttachmentData(Hash128 hash)
		{
#if UNITY_EDITOR
			RemoveAttachmentDataInternal(hash);
			Persist();
#endif
		}
		
		public bool LoadAttachmentData(Hash128 hash, out SkinAttachmentPose[] poses, out SkinAttachmentItem[] items)
		{
			EnsureEntryLookup();
			if (dataStorageLookup.TryGetValue(hash, out DataStorageHeader storageEntry))
			{
				items = storageEntry.reference.items;
				poses = storageEntry.reference.poses;
				return true;
			}
			else
			{
				poses = null;
				items = null;
				return false;
			}
		}
		
		private void Awake()
		{
			EnsureEntryLookup();
			
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
		
		private void EnsureEntryLookup()
		{
			if (dataStorageLookup == null)
			{
				dataStorageLookup = new Dictionary<Hash128, DataStorageHeader>();
				if (databaseEntries != null)
				{
					foreach (var val in databaseEntries)
					{
						dataStorageLookup.Add(val.hashKey, val);
					}
				}
			}

		}
		
#if UNITY_EDITOR
		public void ForceDestroyAttachmentData(Hash128 hash)
		{
			EnsureEntryLookup();
			dataStorageLookup.Remove(hash);
			RemoveFile(hash);
			Persist();
		}
		
		private Hash128 StoreAttachmentDataInternal(SkinAttachmentPose[] poses, SkinAttachmentItem[] items)
		{
			EnsureEntryLookup();

			Hash128 newHash = CalculateHash(poses, items);

			if (dataStorageLookup.TryGetValue(newHash, out DataStorageHeader header))
			{
				header.referenceCount += 1;
				return newHash;
			}
			
			
			
			SkinAttachmentDataEntry assetReference = StoreToFile(newHash, poses, items);

			if (assetReference == null) return default;
			
			DataStorageHeader newEntry = new DataStorageHeader()
			{
				hashKey = newHash,
				referenceCount = 1,
				timeStamp = DateTime.UtcNow.ToLongTimeString(),
				itemCount = items.Length,
				poseCount = poses.Length,
				reference = assetReference
			};

			dataStorageLookup[newHash] = newEntry;

			return newHash;
		}

		private void RemoveAttachmentDataInternal(Hash128 hash)
		{
			EnsureEntryLookup();
			if (dataStorageLookup.TryGetValue(hash, out DataStorageHeader removeStorageEntry))
			{
				--removeStorageEntry.referenceCount;
				if (removeStorageEntry.referenceCount == 0)
				{
					RemoveFile(hash);
					dataStorageLookup.Remove(hash);
				}
			}
		}

		

		private void Persist()
		{
			databaseEntries = dataStorageLookup.Values.ToArray();

			UnityEditor.EditorUtility.SetDirty(this);
			UnityEditor.AssetDatabase.SaveAssets();
			UnityEditor.Undo.ClearUndo(this);

		}

		static string GetFileName(Hash128 hash)
		{
			const string attachmentBinaryDataFilePrefix = "AttachmentData_";
			return attachmentBinaryDataFilePrefix + hash.ToString() + ".asset";
		}

		string GetDataFolderPath()
		{
			var assetPath = AssetDatabase.GetAssetPath(this) ?? null;
			if (assetPath == null) return null;
			var dirName = Path.GetDirectoryName(assetPath);
			if (dirName == null) return null;
			return Path.Combine(dirName,"AttachmentDataStorage");
		}
		
		public SkinAttachmentDataEntry StoreToFile(Hash128 hash, SkinAttachmentPose[] poses, SkinAttachmentItem[] items)
		{
			var fileName = GetFileName(hash);
			var folderPath = GetDataFolderPath();
			if (folderPath == null) return null;
			var filePath = Path.Combine(folderPath, fileName);

			if (!Directory.Exists(folderPath))
			{
				Directory.CreateDirectory(folderPath);
			}

			var dataEntry = CreateInstance<SkinAttachmentDataEntry>();
			dataEntry.hashKey = hash;
			dataEntry.items = items;
			dataEntry.poses = poses;
			
			AssetDatabase.CreateAsset(dataEntry, filePath);

			return dataEntry;
		}

		public bool RemoveFile(Hash128 hash)
		{
			var folderPath = GetDataFolderPath();
			if (folderPath == null) return false;
			var filePath = folderPath + GetFileName(hash);

			if (AssetDatabase.AssetPathExists(filePath))
			{
				AssetDatabase.DeleteAsset(filePath);
				return true;
			}

			return false;
		}
#endif
	}
}
