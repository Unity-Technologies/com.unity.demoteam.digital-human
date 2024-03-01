using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Serialization;

namespace Unity.DemoTeam.DigitalHuman
{
	[CreateAssetMenu(menuName = "Digital Human/Legacy Skin Attachment Data")]
	[PreferBinarySerialization]
	public class LegacySkinAttachmentData : ScriptableObject
	{
		public enum DataVersion
		{
			Version_1,
			Version_2,
			Version_3
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
		public SkinAttachmentItem2[] itemVer2 = null;
		[HideInInspector]
		public SkinAttachmentItem3[] itemVer3 = new SkinAttachmentItem3[16384];
		
		public int itemCount = 0;

		[HideInInspector]
		public int subjectCount = 0;

		public SkinAttachmentItem3[] ItemData => itemVer3;
		public ref SkinAttachmentItem3[] ItemDataRef => ref itemVer3;
		
		private bool CheckMigrationFromVer1()
		{
			if (dataVersion == DataVersion.Version_1 && itemVer1 != null && itemVer1.Length > 0)
			{
				itemVer3 = new SkinAttachmentItem3[itemVer1.Length];

				for (int i = 0; i < itemVer1.Length; ++i)
				{
					var baseNormal = itemVer1[i].baseNormal.sqrMagnitude > 0 ? itemVer1[i].baseNormal : Vector3.up;
					var baseFrame = Quaternion.LookRotation(baseNormal, Vector3.right);
					var baseFrameInv = Quaternion.Inverse(baseFrame);
					
					var targetNormal = itemVer1[i].targetNormal.sqrMagnitude > 0 ? itemVer1[i].targetNormal : Vector3.up;
					var targetFrame = Quaternion.LookRotation(targetNormal, Vector3.right);

					itemVer3[i].poseIndex = itemVer1[i].poseIndex;
					itemVer3[i].poseCount = itemVer1[i].poseCount;
					itemVer3[i].baseVertex = itemVer1[i].baseVertex;
					itemVer3[i].targetFrameW = 1.0f;// used to preserve the sign of the resolved tangent
					itemVer3[i].targetFrameDelta = baseFrameInv * targetFrame;
					itemVer3[i].targetOffset = baseFrameInv * itemVer1[i].targetOffset;

				}

				return true;
			}

			return false;
		}
		
		private bool CheckMigrationFromVer2()
		{
			if (dataVersion == DataVersion.Version_2 && itemVer2 != null && itemVer2.Length > 0)
			{
				itemVer3 = new SkinAttachmentItem3[itemVer2.Length];

				for (int i = 0; i < itemVer2.Length; ++i)
				{

					var baseFrameInv = Quaternion.Inverse(itemVer2[i].baseTangentFrame);

					var targetFrame = itemVer2[i].targetTangentFrame;

					itemVer3[i].poseIndex = itemVer2[i].poseIndex;
					itemVer3[i].poseCount = itemVer2[i].poseCount;
					itemVer3[i].baseVertex = itemVer2[i].baseVertex;
					itemVer3[i].targetFrameW = 1.0f;// used to preserve the sign of the resolved tangent
					itemVer3[i].targetFrameDelta = baseFrameInv * targetFrame;
					itemVer3[i].targetOffset = baseFrameInv * itemVer2[i].targetOffset;

				}

				return true;
			}

			return false;
		}

		private void Awake()
		{
			CheckMigrationFromVer1();
			CheckMigrationFromVer2();
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
			itemVer2 = null;
		}

		public void Persist()
		{
			unsafe
			{
				fixed (SkinAttachmentPose* ptrPose = pose)

				fixed (SkinAttachmentItem3* ptrItemVer3 = itemVer3)

				fixed (ulong* ptrChecksum0 = &checksum0)
				fixed (ulong* ptrChecksum1 = &checksum1)
				{
					HashUnsafeUtilities.ComputeHash128(ptrPose, (ulong)(sizeof(SkinAttachmentPose) * poseCount), ptrChecksum0, ptrChecksum1);
					HashUnsafeUtilities.ComputeHash128(ptrItemVer3, (ulong)(sizeof(SkinAttachmentItem3) * itemCount), ptrChecksum0, ptrChecksum1);

					Debug.LogFormat("SkinAttachmentData changed, new checksum = '{0}' ({1} poses, {2} items)", Checksum(), poseCount, itemCount);
				}
			}

#if UNITY_EDITOR
			dataVersion = DataVersion.Version_3;
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
		public Vector3 targetOffset;
	}
	
	[Serializable]
	public struct SkinAttachmentItem2
	{
		public int poseIndex;
		public int poseCount;
		public int baseVertex;
		public Quaternion baseTangentFrame;
		public Quaternion targetTangentFrame;
		public Vector3 targetOffset;

	}
	
	
	
	[Serializable]
	public struct SkinAttachmentItem3
	{
		public int poseIndex;
		public int poseCount;
		public int baseVertex;
		public float targetFrameW;
		public Quaternion targetFrameDelta;
		public Vector3 targetOffset;//TODO split this into leaf type item that doesn't perform full resolve

	}
}
