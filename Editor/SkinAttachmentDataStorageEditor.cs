using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Unity.DemoTeam.DigitalHuman
{
	using SkinAttachmentItem = SkinAttachmentItem3;
	
	[CustomEditor(typeof(SkinAttachmentDataStorage))]
	public class SkinAttachmentDataStorageEditor : Editor
	{
		private SkinAttachmentDataStorage storage;
		private Vector2 scrollPos = Vector2.zero;
		public override void OnInspectorGUI()
		{
			if (target == null)
				return;

			storage = target as SkinAttachmentDataStorage;
			if (storage == null)
				return;

			var storageEntries = storage.GetAllEntries();
			if (storageEntries == null)
			{
				EditorGUILayout.HelpBox("Storage is empty", MessageType.Info);
				return;
			}
			
			scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
			foreach (var entry in storageEntries)
			{
				storage.LoadAttachmentData(entry.hashKey, out SkinAttachmentPose[] poses, out SkinAttachmentItem[] items);
				DrawGuiStorageEntry(entry, poses, items);
			}
			EditorGUILayout.EndScrollView();
		}

		void DrawGuiStorageEntry(SkinAttachmentDataStorage.DataStorageEntry entry, in SkinAttachmentPose[] poses, in SkinAttachmentItem[] items)
		{
			EditorGUILayout.BeginHorizontal();
			
			EditorGUILayout.HelpBox(
				"hash: " + entry.hashKey
				+ "\nname: " + entry.entryName 
				+ "\nbaked: " + entry.timeStamp
				+ "\nitemCount:" + (items == null ? "0" : + items.Length)
				+ "\nposeCount:" + (poses == null ? "0" : + poses.Length)
				,MessageType.None);
			if (GUILayout.Button("delete"))
			{
				storage.RemoveAttachmentData(entry.hashKey);
				EditorUtility.SetDirty(this);
			}
			EditorGUILayout.EndHorizontal();
		}

	}
}
