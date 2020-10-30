using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Unity.DemoTeam.DigitalHuman
{
	public static class SkinDeformationClipRegions
	{
		private static bool active;

		private static SkinDeformationClip clip;

		private static TextAsset[] activeDenoiseIndices = null;
		private static TextAsset[] activeTransplantIndices = null;

		private static int[] pairedDenoiseIndices = new int[0];
		private static int[] pairedTransplantIndices = new int[0];

		static bool CompareTextAssetArrays(TextAsset[] a, TextAsset[] b)
		{
			if (a == null && b == null)
				return true;

			if (a != b)
				return false;

			if (a.Length != b.Length)
				return false;

			for (int i = 0; i != a.Length; i++)
			{
				if (a[i] != b[i])
					return false;
			}

			return true;
		}

		static int[] BuildPairsFromIndices(MeshAdjacency meshAdjacency, int[] indices)
		{
			var pairs = new List<int>();
			foreach (int i in indices)
			{
				foreach (int j in meshAdjacency.vertexVertices[i])
				{
					pairs.Add(i);
					pairs.Add(j);
				}
			}
			return pairs.ToArray();
		}

		static void OnSceneGUI(SceneView sceneView)
		{
			if (Event.current.type != EventType.Repaint)
				return;

			var mesh = clip.settings.transferTarget;
			if (mesh == null)
				return;

			var updateDenoiseIndices = !CompareTextAssetArrays(activeDenoiseIndices, clip.settings.denoiseRegions);
			var updateTransplantIndices = !CompareTextAssetArrays(activeTransplantIndices, clip.settings.transplantRegions);

			foreach (var deformationRenderer in SkinDeformationRenderer.enabledInstances)
			{
				if (deformationRenderer.meshAsset != mesh)
					continue;

				var target = deformationRenderer.GetComponent<SkinAttachmentTarget>();
				if (target == null)
					continue;

				var targetMeshInfo = target.GetCachedMeshInfo();
				if (targetMeshInfo.valid == false)
					continue;

				if (updateDenoiseIndices)
				{
					updateDenoiseIndices = false;
					activeDenoiseIndices = clip.settings.denoiseRegions.Clone() as TextAsset[];
					pairedDenoiseIndices = BuildPairsFromIndices(targetMeshInfo.meshAdjacency, SkinDeformationClipEditor.ResolveIndexArrayFromVertexSelectionArray(activeDenoiseIndices));
				}

				if (updateTransplantIndices)
				{
					updateTransplantIndices = false;
					activeTransplantIndices = clip.settings.transplantRegions.Clone() as TextAsset[];
					pairedTransplantIndices = BuildPairsFromIndices(targetMeshInfo.meshAdjacency, SkinDeformationClipEditor.ResolveIndexArrayFromVertexSelectionArray(activeTransplantIndices));
				}

				var meshMatrix = deformationRenderer.transform.localToWorldMatrix;
				var meshBuffers = targetMeshInfo.meshBuffers;

				using (var scope = new Handles.DrawingScope(Color.Lerp(Color.clear, Color.magenta, 0.5f), meshMatrix))
				{
					Handles.DrawLines(meshBuffers.vertexPositions, pairedDenoiseIndices);
				}

				using (var scope = new Handles.DrawingScope(Color.Lerp(Color.clear, Color.green, 0.5f), meshMatrix))
				{
					Handles.DrawLines(meshBuffers.vertexPositions, pairedTransplantIndices);
				}
			}
		}

		public static void Enable(SkinDeformationClip clip)
		{
			SkinDeformationClipRegions.clip = clip;

			if (active == false)
			{
				active = true;
				SceneView.duringSceneGui += OnSceneGUI;
				SceneView.RepaintAll();
			}
		}

		public static void Disable()
		{
			SkinDeformationClipRegions.clip = null;

			if (active)
			{
				active = false;
				SceneView.duringSceneGui -= OnSceneGUI;
				SceneView.RepaintAll();
			}
		}
	}
}
