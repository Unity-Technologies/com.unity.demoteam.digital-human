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

			//Prune
			{
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.HelpBox("Pruning entries might take considerable amount of time", MessageType.Warning);
				if (GUILayout.Button("Prune Entries"))
				{
					var allReferences = GetAllReferencedAttachmentData();
					var storagePath = AssetDatabase.GetAssetPath(storage);
					if (allReferences.TryGetValue(storagePath, out var referencesList))
					{
						HashSet<Hash128> referencesSet = new HashSet<Hash128>(referencesList);
						foreach (var entryInStorage in storageEntries)
						{
							if (!referencesSet.Contains(entryInStorage.hashKey))
							{
								storage.ForceDestroyAttachmentData(entryInStorage.hashKey);
							}
						}
						EditorUtility.SetDirty(this);
					}
				}
				EditorGUILayout.EndHorizontal();
			}
			
			

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

		Dictionary<string, List<Hash128>> GetAllReferencedAttachmentData()
		{
			List<string> prefabs = new List<string>();
			List<string> scenes = new List<string>();
			
			var allAssets = AssetDatabase.GetAllAssetPaths();
			foreach(var s in allAssets)
			{
				var ext = Path.GetExtension(s);

				if (ext.Equals(".prefab"))
				{
					prefabs.Add(s);
				}
				else if (ext.Equals(".unity"))
				{
					scenes.Add(s);	
				}
			}

			Dictionary<string, List<Hash128>> hashesUsed = new Dictionary<string, List<Hash128>>(64);
			GatherUsedHashesFromPrefabs(prefabs, ref hashesUsed);
			GatherUsedHashesFromScenes(scenes, ref hashesUsed);
			return hashesUsed;
		}

		void GatherUsedHashesFromPrefabs(List<string> prefabsList, ref Dictionary<string, List<Hash128>> hashesUsed)
		{
			foreach (var prefabPath in prefabsList)
			{
				try
				{
					GameObject prefabContainer = PrefabUtility.LoadPrefabContents(prefabPath);
					
					if(prefabContainer == null) continue;
					
					var attachments = prefabContainer.GetComponentsInChildren<SkinAttachmentComponentCommon.ISkinAttachmentComponent>(true);
					
					foreach ( SkinAttachmentComponentCommon.ISkinAttachmentComponent c in attachments )
					{
						var storage = c.GetCommonComponent().dataStorage;
						var hash = c.GetCommonComponent().CheckSum;
						var path = AssetDatabase.GetAssetPath(storage);
	
						if (hashesUsed.TryGetValue(path, out var hashList))
						{
							hashList.Add(hash);
						}
						else
						{
							var newList = new List<Hash128>();
							newList.Add(hash);
							hashesUsed.Add(path, newList);
						}
						
					}
					
					PrefabUtility.UnloadPrefabContents(prefabContainer);
				}
				catch (Exception e)
				{
					
				}
			}
		}
		
		void GatherUsedHashesFromScenes(List<string> scenesList,  ref Dictionary<string, List<Hash128>> hashesUsed)
		{
			foreach (var scenePath in scenesList)
			{
				try
				{
					Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);
					GameObject[] rootGOs = scene.GetRootGameObjects();
					
					foreach (GameObject go in rootGOs)
					{
						var attachments = go.GetComponentsInChildren<SkinAttachmentComponentCommon.ISkinAttachmentComponent>(true);
						foreach ( SkinAttachmentComponentCommon.ISkinAttachmentComponent c in attachments )
						{
							var storage = c.GetCommonComponent().dataStorage;
							var hash = c.GetCommonComponent().CheckSum;
							var path = AssetDatabase.GetAssetPath(storage);
							if (hashesUsed.TryGetValue(path, out var hashList))
							{
								hashList.Add(hash);
							}
							else
							{
								var newList = new List<Hash128>();
								newList.Add(hash);
								hashesUsed.Add(path, newList);
							}
						}
					}
					EditorSceneManager.ClosePreviewScene(scene);
				}
				catch (Exception e)
				{
					
				}
			}
		}
	}
}
