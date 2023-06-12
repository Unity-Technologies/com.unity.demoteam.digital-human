using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	using SkinAttachmentItem = SkinAttachmentItem3;
	
	[CreateAssetMenu(menuName = "Digital Human/Skin Attachment Data Storage")]
	[PreferBinarySerialization]
	public class SkinAttachmentDataStorage : ScriptableObject
	{

		[Serializable]
		private class DataStorageEntry
		{
			public Hash128 hashKey;
			public int dataStorageIndex;
		}

		[Serializable]
		private struct SkinAttachmentData
		{
			public Hash128 hashKey;
			public SkinAttachmentPose[] poses;
			public SkinAttachmentItem[] items;
		}
		
		[SerializeField][HideInInspector]
		private SkinAttachmentData[] dataStorage;
		[SerializeField][ReadOnlyPropertyAttribute]
		private DataStorageEntry[] databaseEntries ;

		[NonSerialized] private Dictionary<Hash128, DataStorageEntry> dataStorageLookup;

		public Hash128 StoreAttachmentData(SkinAttachmentPose[] poses, SkinAttachmentItem[] items)
		{
			Hash128 h = StoreAttachmentDataInternal(poses, items);
			Persist();
			return h;
		}
		
		public void RemoveAttachmentData(Hash128 hash)
		{
			RemoveAttachmentDataInternal(hash);
			Persist();
		}
		
		public Hash128 UpdateAttachmentData(SkinAttachmentPose[] poses, SkinAttachmentItem[] items, Hash128 oldHash)
		{
			RemoveAttachmentData(oldHash);
			Hash128 h = StoreAttachmentDataInternal(poses, items);
			Persist();
			return h;
		}

		public bool LoadAttachmentData(Hash128 hash, out SkinAttachmentPose[] poses, out SkinAttachmentItem[] items)
		{
			EnsureEntryLookup();
			if (dataStorageLookup.TryGetValue(hash, out DataStorageEntry storageEntry))
			{
				items = dataStorage[storageEntry.dataStorageIndex].items;
				poses = dataStorage[storageEntry.dataStorageIndex].poses;
				return true;
			}
			else
			{
				poses = null;
				items = null;
				return false;
			}
		}
		
		private Hash128 StoreAttachmentDataInternal(SkinAttachmentPose[] poses, SkinAttachmentItem[] items)
		{
			EnsureEntryLookup();
			
			ArrayUtils.ResizeChecked(ref databaseEntries, (databaseEntries?.Length ?? 0) + 1);
			ArrayUtils.ResizeChecked(ref dataStorage, (dataStorage?.Length ?? 0) + 1);
			
			Hash128 newHash = CalculateHash(poses, items);
			
			DataStorageEntry entry = databaseEntries[^1];
			SkinAttachmentData data = new SkinAttachmentData
			{
				hashKey = newHash
			};
			ArrayUtils.CopyChecked(poses, ref data.poses, poses.Length);
			ArrayUtils.CopyChecked(items, ref data.items, items.Length);
			dataStorage[^1] = data;
			
			
			DataStorageEntry newEntry = new DataStorageEntry()
			{
				hashKey = newHash,
				dataStorageIndex = dataStorage.Length - 1
			};


			dataStorageLookup[newHash] = newEntry;

			return newHash;
		}

		private void RemoveAttachmentDataInternal(Hash128 hash)
		{
			EnsureEntryLookup();
			if (dataStorageLookup.TryGetValue(hash, out DataStorageEntry removeStorageEntry))
			{
				//swap removed entry with last one and fixup the datastorage index
				if (dataStorage.Length > 1)
				{
					Hash128 lastEntryHash = dataStorage[^1].hashKey;
					if (dataStorageLookup.TryGetValue(hash, out DataStorageEntry lastEntry))
					{
						lastEntry.dataStorageIndex = removeStorageEntry.dataStorageIndex;
					}
					dataStorage[removeStorageEntry.dataStorageIndex] = dataStorage[^1];
				}
				
				//remove this entry
				dataStorageLookup.Remove(hash);
				if (dataStorage.Length > 1)
				{
					ArrayUtils.ResizeChecked(ref dataStorage, dataStorage.Length - 1);
				}
				else
				{
					dataStorage = null;
				}
				
			}
			else
			{
				Debug.LogWarning("Tried to remove storage data entry which doesn't exists, ignoring.");
			}
		}

		private void Awake()
		{
			EnsureEntryLookup();
			
		}

		private void Persist()
		{
			databaseEntries = dataStorageLookup.Values.ToArray();
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
			UnityEditor.AssetDatabase.SaveAssets();
			UnityEditor.Undo.ClearUndo(this);
#endif
		}

		private void EnsureEntryLookup()
		{
			if (dataStorageLookup == null)
			{
				dataStorageLookup = new Dictionary<Hash128, DataStorageEntry>();
				if (databaseEntries != null)
				{
					foreach (var val in databaseEntries)
					{
						dataStorageLookup.Add(val.hashKey, val);
					}
				}
			}

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
