using System;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	[CreateAssetMenu(menuName = "Digital Human/Skin Attachment Data")]
	[PreferBinarySerialization]
	public class SkinAttachmentData : ScriptableObject
	{
		[HideInInspector]
		public SkinAttachmentPose[] pose = new SkinAttachmentPose[131072];
		public int poseCount = 0;

		[HideInInspector]
		public SkinAttachmentItem[] item = new SkinAttachmentItem[16384];
		public int itemCount = 0;

		public void Clear()
		{
			poseCount = 0;
			itemCount = 0;
		}

		public void Persist()
		{
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
			UnityEditor.AssetDatabase.SaveAssets();
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
		public Vector3 targetOffset;// TODO split this into leaf type item that doesn't perform full resolve
	}
}