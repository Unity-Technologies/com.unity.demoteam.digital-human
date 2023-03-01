using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Serialization;

namespace Unity.DemoTeam.DigitalHuman
{
	[CreateAssetMenu(menuName = "Digital Human/Skin Attachment Data")]
	[PreferBinarySerialization]
	public class SkinAttachmentData : ScriptableObject
	{
		public enum DataVersion
		{
			Version_1,
			Version_2
		}
		//[HideInInspector]
		//public int driverChecksum = -1;
		[HideInInspector]
		public DataVersion dataVersion = DataVersion.Version_1;
		[HideInInspector]
		public int driverVertexCount = 0;

		[HideInInspector]
		public ulong checksum0 = 0;
		[HideInInspector]
		public ulong checksum1 = 0;

		[HideInInspector]
		public SkinAttachmentPose[] pose = new SkinAttachmentPose[131072];
		public int poseCount = 0;

		[HideInInspector] [FormerlySerializedAs("item")]
		public SkinAttachmentItem1[] itemVer1 = null;
		[HideInInspector]
		public SkinAttachmentItem2[] itemVer2 = new SkinAttachmentItem2[16384];
		
		public int itemCount = 0;

		[HideInInspector]
		public int subjectCount = 0;

		public SkinAttachmentItem2[] ItemData => itemVer2;
		public ref SkinAttachmentItem2[] ItemDataRef => ref itemVer2;
		
		private bool CheckMigrationFromVer1()
		{
			if (dataVersion == DataVersion.Version_1 && itemVer1 != null && itemVer1.Length > 0)
			{
				itemVer2 = new SkinAttachmentItem2[itemVer1.Length];

				for (int i = 0; i < itemVer1.Length; ++i)
				{
					itemVer2[i].poseIndex = itemVer1[i].poseIndex;
					itemVer2[i].poseCount = itemVer1[i].poseCount;
					itemVer2[i].baseVertex = itemVer1[i].baseVertex;
					itemVer2[i].baseTangentFrame = Quaternion.LookRotation(Vector3.forward, itemVer1[i].baseNormal);
					itemVer2[i].targetTangentFrame = Quaternion.LookRotation(Vector3.forward, itemVer1[i].targetNormal);
					itemVer2[i].targetOffset = itemVer1[i].targetOffset;

				}

				return true;
			}

			return false;
		}

		private void Awake()
		{
			CheckMigrationFromVer1();
		}

		public Hash128 Checksum()
		{
			return new Hash128(checksum0, checksum1);
		}

		public void Clear()
		{
			driverVertexCount = 0;
			checksum0 = 0;
			checksum1 = 0;
			poseCount = 0;
			itemCount = 0;
			subjectCount = 0;
			itemVer1 = null;
		}

		public void Persist()
		{
			unsafe
			{
				fixed (SkinAttachmentPose* ptrPose = pose)
				fixed (SkinAttachmentItem1* ptrItemVer1 = itemVer1)
				fixed (SkinAttachmentItem2* ptrItemVer2 = itemVer2)

				fixed (ulong* ptrChecksum0 = &checksum0)
				fixed (ulong* ptrChecksum1 = &checksum1)
				{
					HashUnsafeUtilities.ComputeHash128(ptrPose, (ulong)(sizeof(SkinAttachmentPose) * poseCount), ptrChecksum0, ptrChecksum1);
					switch (dataVersion)
					{
						case DataVersion.Version_1:
							HashUnsafeUtilities.ComputeHash128(ptrItemVer1, (ulong)(sizeof(SkinAttachmentItem1) * itemCount), ptrChecksum0, ptrChecksum1);
							break;
						
						case DataVersion.Version_2:
							HashUnsafeUtilities.ComputeHash128(ptrItemVer2, (ulong)(sizeof(SkinAttachmentItem2) * itemCount), ptrChecksum0, ptrChecksum1);
							break;
					}
					
					Debug.LogFormat("SkinAttachmentData changed, new checksum = '{0}' ({1} poses, {2} items)", Checksum(), poseCount, itemCount);
				}
			}

#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
			UnityEditor.AssetDatabase.SaveAssets();
			UnityEditor.Undo.ClearUndo(this);
#endif
		}
	}

	[Serializable]
	public struct SkinAttachmentPose
	{
		public int v0;
		public int v1;
		public int v2;
		public float area;
		public float targetDist;
		public Barycentric targetCoord;
	}

	[Serializable]
	public struct SkinAttachmentItem1
	{
		public int poseIndex;
		public int poseCount;
		public int baseVertex;
		public Vector3 baseNormal;
		public Vector3 targetNormal;
		public Vector3 targetOffset;//TODO split this into leaf type item that doesn't perform full resolve
	}
	[Serializable]
	public struct SkinAttachmentItem2
	{
		public int poseIndex;
		public int poseCount;
		public int baseVertex;
		public Quaternion baseTangentFrame;
		public Quaternion targetTangentFrame;
		public Vector3 targetOffset;//TODO split this into leaf type item that doesn't perform full resolve
		
	}
}
