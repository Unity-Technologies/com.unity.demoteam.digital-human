using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SkinAttachmentTarget))]
	public class SkinAttachmentTargetEditor : Editor
	{
		private HashSet<SkinAttachmentTarget> attachmentTargetSet = new HashSet<SkinAttachmentTarget>();

		public override void OnInspectorGUI()
		{
			attachmentTargetSet.Clear();

			if (target == null)
				return;

			var driver = target as SkinAttachmentTarget;
			if (driver == null)
				return;

			if (driver.attachData == null)
			{
				EditorGUILayout.HelpBox("Must bind SkinAttachmentData asset before use.", MessageType.Error);
				base.OnInspectorGUI();
				return;
			}

			EditorGUILayout.HelpBox("Currently bound to " + driver.attachData + "\nChecksum: " + driver.attachData.Checksum(), MessageType.Info);
			DrawGUIAttachmentData(driver.attachData);
			DrawGUIAttachmentDataValidation(driver);
			EditorGUILayout.Separator();

			base.OnInspectorGUI();
			EditorGUILayout.Separator();

			GUILayout.Label(string.Format("Attachments ({0})", driver.subjects.Count), EditorStyles.boldLabel);
			var checksum = driver.attachData.Checksum();
			var checksumFailed = driver.CommitRequired();
			for (int i = 0; i != driver.subjects.Count; i++)
			{
				var attachment = driver.subjects[i];

				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.ObjectField(attachment, typeof(SkinAttachment), false);
				SkinAttachmentEditor.DrawGUIDetach(attachment, attachmentTargetSet);
				EditorGUILayout.EndHorizontal();

				if (checksumFailed)
				{
					if (checksum != attachment.Checksum())
					{
						Color color = GUI.color;
						GUI.color = Color.red;
						GUILayout.Label("Checksum FAILED:  " + attachment.Checksum(), EditorStyles.helpBox);
						GUI.color = color;
					}
					else
					{
						GUILayout.Label("Checksum passed:  " + attachment.Checksum(), EditorStyles.helpBox);
					}
				}

				if (i >= driver.subjects.Count || driver.subjects[i] != attachment)
					i--;
			}

			SkinAttachmentEditor.CommitTargetChanges(attachmentTargetSet);
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

		void DrawGUIAttachmentDataValidation(SkinAttachmentTarget driver)
		{
			var checksum = driver.attachData.Checksum();
			var checksumFailed = driver.CommitRequired();
			if (checksumFailed)
			{
				EditorGUILayout.HelpBox("Rebuild required: Checksum of driver or one or more subjects does not match checksum of data.", MessageType.Warning);
			}
			else if (driver.attachData.subjectCount != driver.subjects.Count)
			{
				EditorGUILayout.HelpBox("Rebuild suggested: Data contains poses that are no longer referenced.", MessageType.Warning);
			}

			if (GUILayout.Button("Rebuild"))
			{
				driver.CommitSubjects();
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
			}
		}
	}
}
