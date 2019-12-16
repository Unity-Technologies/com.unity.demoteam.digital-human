using UnityEngine;
using UnityEditor;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SkinAttachmentTarget))]
	public class SkinAttachmentTargetEditor : Editor
	{
		bool showAttachments;

		public override void OnInspectorGUI()
		{
			if (target == null)
				return;

			var driver = target as SkinAttachmentTarget;
			if (driver == null)
				return;

			if (driver.attachData == null)
			{
				EditorGUILayout.HelpBox("Must bind SkinAttachmentData asset before use.", MessageType.Error);
				return;
			}

			EditorGUILayout.HelpBox("Bound to " + driver.attachData, MessageType.Info);
			DrawGUIAttachmentData(driver.attachData);
			EditorGUILayout.Separator();

			base.OnInspectorGUI();
			EditorGUILayout.Separator();

			GUILayout.Label("Attachments", EditorStyles.boldLabel);
			foreach (var attachment in driver.subjects)
			{
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.ObjectField(attachment, typeof(SkinAttachment), false);
				SkinAttachmentEditor.DrawGUIDetach(attachment);
				EditorGUILayout.EndHorizontal();
			}
		}

		void DrawGUIAttachmentData(SkinAttachmentData attachData)
		{
			if (DrawGUIGrowShrink("Poses", ref attachData.pose, attachData.poseCount))
			{
				attachData.Persist();
			}

			if (DrawGUIGrowShrink("Items" , ref attachData.item, attachData.itemCount))
			{
				attachData.Persist();
			}
		}

		bool DrawGUIGrowShrink<T>(string label, ref T[] buffer, int count)
		{
			var capacity = buffer.Length;
			var capacityChanged = false;

			EditorGUILayout.BeginHorizontal();
			{
				GUILayout.Button(string.Empty, GUILayout.ExpandWidth(true));
				EditorGUI.ProgressBar(GUILayoutUtility.GetLastRect(), count / (float)capacity, label + ": " + count + " / " + capacity);

				if (GUILayout.Button("Grow", GUILayout.ExpandWidth(false)))
				{
					ArrayUtils.ResizeChecked(ref buffer, buffer.Length * 2);
					capacityChanged = true;
				}

				EditorGUI.BeginDisabledGroup(count > capacity / 2);
				if (GUILayout.Button("Shrink", GUILayout.ExpandWidth(false)))
				{
					ArrayUtils.ResizeChecked(ref buffer, buffer.Length / 2);
					capacityChanged = true;
				}
				EditorGUI.EndDisabledGroup();
			}
			EditorGUILayout.EndHorizontal();

			return capacityChanged;
		}

		void OnSceneGUI()
		{
			var driver = target as SkinAttachmentTarget;
			if (driver == null)
				return;

			if (driver.showWireframe)
			{

			}

			if (driver.showMouseOver)
			{
				DrawSceneGUIMouseOver(driver);
			}
		}

		public static void DrawSceneGUIMouseOver(SkinAttachmentTarget driver)
		{
			var mouseScreen = Event.current.mousePosition;
			var mouseWorldRay = HandleUtility.GUIPointToWorldRay(mouseScreen);

			var objectRayPos = driver.transform.InverseTransformPoint(mouseWorldRay.origin);
			var objectRayDir = driver.transform.InverseTransformDirection(mouseWorldRay.direction);

			var meshInfo = driver.GetCachedMeshInfo();
			if (meshInfo.valid == false)
				return;

			var vertex = meshInfo.meshVertexBSP.RaycastApprox(ref objectRayPos, ref objectRayDir, 10);
			if (vertex == -1)
				return;

			using (var scope = new Handles.DrawingScope(Color.blue, driver.transform.localToWorldMatrix))
			{
				const int maxDepth = 3;

				var triangleColor = Handles.color;
				var triangleDepth = 0;

				using (var triangleBFS = new UnsafeBFS(meshInfo.meshAdjacency.triangleCount))
				{
					foreach (var triangle in meshInfo.meshAdjacency.vertexTriangles[vertex])
					{
						triangleBFS.Insert(triangle);
					}

					while (triangleBFS.MoveNext() && triangleBFS.depth < maxDepth)
					{
						if (triangleDepth < triangleBFS.depth)
						{
							triangleDepth = triangleBFS.depth;
							Handles.color = Color.Lerp(triangleColor, Color.clear, Mathf.InverseLerp(0, maxDepth, triangleDepth));
						}

						var _e = meshInfo.meshAdjacency.triangleVertices[triangleBFS.position].GetEnumerator();
						int v0 = _e.ReadNext();
						int v1 = _e.ReadNext();
						int v2 = _e.ReadNext();

						Handles.DrawLine(meshInfo.meshBuffers.vertexPositions[v0], meshInfo.meshBuffers.vertexPositions[v1]);
						Handles.DrawLine(meshInfo.meshBuffers.vertexPositions[v1], meshInfo.meshBuffers.vertexPositions[v2]);
						Handles.DrawLine(meshInfo.meshBuffers.vertexPositions[v2], meshInfo.meshBuffers.vertexPositions[v0]);

						foreach (var triangle in meshInfo.meshAdjacency.triangleTriangles[triangleBFS.position])
						{
							triangleBFS.Insert(triangle);
						}
					}
				}

				//var vertexPosition = meshInfo.meshBuffers.vertexPositions[vertex];
				//var vertexNormal = meshInfo.meshBuffers.vertexNormals[vertex];

				//Handles.DrawWireDisc(vertexPosition, vertexNormal, 0.005f);
				//Handles.DrawLine(vertexPosition, vertexPosition + 0.0025f * vertexNormal);
			}
		}
	}
}
