using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	[CreateAssetMenu(menuName = "Digital Human/Skin Attachment Data")]
	[PreferBinarySerialization]
	public class SkinAttachmentData : ScriptableObject
	{
		//[HideInInspector]
		//public int driverChecksum = -1;
		[HideInInspector]
		public int driverVertexCount = 0;

		[HideInInspector]
		public ulong checksum0 = 0;
		[HideInInspector]
		public ulong checksum1 = 0;

		[HideInInspector]
		public SkinAttachmentPose[] pose = new SkinAttachmentPose[131072];
		public int poseCount = 0;

		[HideInInspector]
		public SkinAttachmentItem[] item = new SkinAttachmentItem[16384];
		public int itemCount = 0;

		[HideInInspector]
		public int subjectCount = 0;

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
		}

		public void Persist()
		{
			unsafe
			{
				fixed (SkinAttachmentPose* ptrPose = pose)
				fixed (SkinAttachmentItem* ptrItem = item)
				fixed (ulong* ptrChecksum0 = &checksum0)
				fixed (ulong* ptrChecksum1 = &checksum1)
				{
					HashUnsafeUtilities.ComputeHash128(ptrPose, (ulong)(sizeof(SkinAttachmentPose) * poseCount), ptrChecksum0, ptrChecksum1);
					HashUnsafeUtilities.ComputeHash128(ptrItem, (ulong)(sizeof(SkinAttachmentItem) * itemCount), ptrChecksum0, ptrChecksum1);
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
	public struct SkinAttachmentItem
	{
		public int poseIndex;
		public int poseCount;
		public int baseVertex;
		public Vector3 baseNormal;
		public Vector3 targetNormal;
		public Vector3 targetOffset;//TODO split this into leaf type item that doesn't perform full resolve
	}
}
