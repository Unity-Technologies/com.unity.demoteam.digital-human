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
			public int itemOffset;
			public int itemCount;
			public int poseOffset;
			public int poseCount;
		}
		[SerializeField][HideInInspector]
		private SkinAttachmentPose[] posesDatabase;
		[SerializeField][HideInInspector]
		private SkinAttachmentItem[] itemsDatabase;
		[SerializeField][ReadOnlyPropertyAttribute]
		private DataStorageEntry[] databaseEntries;

		[NonSerialized] private Dictionary<Hash128, DataStorageEntry> dataStorageLookup;

		public Hash128 StoreAttachmentData(SkinAttachmentPose[] poses, SkinAttachmentItem[] items)
		{
			Hash128 h = StoreAttachmentDataInternal(poses, items);
			Persist();
			return h;
		}
		
		public void RemoveAttachmentData(Hash128 hash)
		{
			RemoveAttachmentData(hash);
			Persist();
		}
		
		public Hash128 UpdateAttachmentData(SkinAttachmentPose[] poses, SkinAttachmentItem[] items, Hash128 oldHash)
		{
			RemoveAttachmentData(oldHash);
			Hash128 h = StoreAttachmentDataInternal(poses, items);
			Persist();
			return h;
		}

		private Hash128 StoreAttachmentDataInternal(SkinAttachmentPose[] poses, SkinAttachmentItem[] items)
		{
			Hash128 newHash = CalculateHash(poses, items);
			DataStorageEntry newEntry = new DataStorageEntry()
			{
				hashKey = newHash,
				itemOffset = itemsDatabase.Length,
				poseOffset = posesDatabase.Length,
				itemCount = items.Length,
				poseCount = poses.Length
			};
			ArrayUtils.ResizeChecked(ref databaseEntries, databaseEntries.Length + 1);
			ArrayUtils.CopyChecked(poses, ref posesDatabase, poses.Length);
			ArrayUtils.CopyChecked(items, ref itemsDatabase, items.Length);

			databaseEntries[^1] = newEntry;

			return newHash;
		}

		private void RemoveAttachmentDataInternal(Hash128 hash)
		{
			if (dataStorageLookup.TryGetValue(hash, out DataStorageEntry removeStorageEntry))
			{
				ArrayUtils.RemoveRange(ref itemsDatabase, removeStorageEntry.itemOffset, removeStorageEntry.itemCount);
				ArrayUtils.RemoveRange(ref posesDatabase, removeStorageEntry.poseOffset, removeStorageEntry.poseCount);

				//remove this entry
				dataStorageLookup.Remove(hash);
				
				//correct offset for entries after this, since the slice/range has been removed from data
				foreach (var lookup in dataStorageLookup)
				{
					if (lookup.Value.itemOffset > removeStorageEntry.itemOffset)
					{
						lookup.Value.itemOffset -= removeStorageEntry.itemCount;
					}
					
					if (lookup.Value.poseOffset > removeStorageEntry.poseOffset)
					{
						lookup.Value.poseOffset -= removeStorageEntry.poseCount;
					}
				}
				
			}
			else
			{
				Debug.LogError("Tried to remove storage data entry which doesn't exists, ignoring.");
			}
		}

		private void Awake()
		{
			dataStorageLookup = new Dictionary<Hash128, DataStorageEntry>();
			foreach (var val in databaseEntries)
			{
				dataStorageLookup.Add(val.hashKey, val);
			}
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
