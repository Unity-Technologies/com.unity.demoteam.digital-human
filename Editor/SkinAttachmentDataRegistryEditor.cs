using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Unity.DemoTeam.DigitalHuman
{
	using SkinAttachmentItem = SkinAttachmentItem3;
	
	[CustomEditor(typeof(SkinAttachmentDataRegistry))]
	public class SkinAttachmentDataRegistryEditor : Editor
	{
		private Vector2 scrollPos = Vector2.zero;
		public override void OnInspectorGUI()
		{
			if (target == null)
				return;

			var storage = target as SkinAttachmentDataRegistry;
			if (storage == null)
				return;

			var storageEntries = storage.GetAllEntries();
			if (storageEntries == null)
			{
				EditorGUILayout.HelpBox("Storage is empty", MessageType.Info);
				return;
			}
			
			EditorGUILayout.HelpBox($"Number of entries: {storageEntries.Length} ", MessageType.Info);
			scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
			foreach (var entry in storageEntries)
			{
				DrawGuiStorageEntry(storage, entry);
			}
			EditorGUILayout.EndScrollView();
		}

		void DrawGuiStorageEntry(SkinAttachmentDataRegistry storage, SkinAttachmentDataRegistry.DataStorageHeader entry)
		{
			EditorGUILayout.BeginHorizontal();

			bool missing = entry.reference == null || entry.reference.items == null || entry.reference.poses == null;

			string statusString = missing ? "DATA MISSING OR CORRUPT!" : "";
			
			EditorGUILayout.HelpBox($"hash: {entry.hashKey}\nbaked: {entry.timeStamp}\nitemCount:{entry.itemCount}\nposeCount:{entry.poseCount}\n{statusString}"
				,missing ? MessageType.Error : MessageType.None);
			if (GUILayout.Button("delete"))
			{
				storage.ForceDestroyAttachmentData(entry.hashKey);
				EditorUtility.SetDirty(this);
			}
			EditorGUILayout.EndHorizontal();
		}

	}
}
