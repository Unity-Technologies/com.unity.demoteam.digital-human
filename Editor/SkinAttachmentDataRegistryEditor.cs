using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Unity.DemoTeam.DigitalHuman
{
	using SkinAttachmentItem = SkinAttachmentItem3;
	
	[CustomEditor(typeof(SkinAttachmentDataRegistry))]
	public class SkinAttachmentDataRegistryEditor : Editor
	{
		private SkinAttachmentDataRegistry storage;
		private Vector2 scrollPos = Vector2.zero;
		public override void OnInspectorGUI()
		{
			if (target == null)
				return;

			storage = target as SkinAttachmentDataRegistry;
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
				DrawGuiStorageEntry(entry);
			}
			EditorGUILayout.EndScrollView();
		}

		void DrawGuiStorageEntry(SkinAttachmentDataRegistry.DataStorageHeader entry)
		{
			EditorGUILayout.BeginHorizontal();
			
			EditorGUILayout.HelpBox(
				"hash: " + entry.hashKey
				+ "\nbaked: " + entry.timeStamp
				+ "\nitemCount:" + entry.itemCount
				+ "\nposeCount:" + entry.poseCount
				,MessageType.None);
			if (GUILayout.Button("delete"))
			{
				storage.ForceDestroyAttachmentData(entry.hashKey);
				EditorUtility.SetDirty(this);
			}
			EditorGUILayout.EndHorizontal();
		}

	}
}
