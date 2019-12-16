using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SkinAttachment)), CanEditMultipleObjects]
	public class SkinAttachmentEditor : Editor
	{
		private Editor attachmentTargetEditor;

		public override void OnInspectorGUI()
		{
			if (target == null)
				return;

			if (targets.Length == 1)
			{
				var attachment = target as SkinAttachment;
				if (attachment == null)
					return;

				EditorGUILayout.HelpBox(attachment.attached ? "Attached to " + attachment.target : "Detached", MessageType.Info);
				DrawGUIAttachDetach(attachment);
				EditorGUILayout.Separator();

				base.OnInspectorGUI();

				var attachmentTarget = (target as SkinAttachment).target;
				if (attachmentTarget != null)
				{
					Editor.CreateCachedEditor(attachmentTarget, null, ref attachmentTargetEditor);
					attachmentTargetEditor.DrawHeader();
					attachmentTargetEditor.OnInspectorGUI();
				}
			}
			else
			{
				EditorGUILayout.HelpBox("Multiple attachments selected", MessageType.Warning);

				foreach (var target in targets)
				{
					var attachment = target as SkinAttachment;
					if (attachment == null)
						continue;

					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.ObjectField(attachment, typeof(SkinAttachment), false);
					DrawGUIAttachDetach(attachment);
					EditorGUILayout.EndHorizontal();
				}
			}
		}

		public static void DrawGUIAttachDetach(SkinAttachment attachment)
		{
			EditorGUILayout.BeginVertical();
			DrawGUIAttach(attachment);
			DrawGUIDetach(attachment);
			EditorGUILayout.EndVertical();
		}

		public static void DrawGUIAttach(SkinAttachment attachment)
		{
			EditorGUI.BeginDisabledGroup(attachment.attached);
			{
				if (GUILayout.Button("Attach"))
				{
					attachment.attached = true;
				}
			}
			EditorGUI.EndDisabledGroup();
		}

		public static void DrawGUIDetach(SkinAttachment attachment)
		{
			EditorGUI.BeginDisabledGroup(!attachment.attached);
			{
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Detach"))
				{
					attachment.attached = false;
					attachment.preserveResolved = false;
				}
				if (GUILayout.Button("+ Hold", GUILayout.ExpandWidth(false)))
				{
					attachment.attached = false;
					attachment.preserveResolved = true;
				}
				EditorGUILayout.EndHorizontal();
			}
			EditorGUI.EndDisabledGroup();
		}
	}
}
