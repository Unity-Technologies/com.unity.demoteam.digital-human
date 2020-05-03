using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SkinAttachment)), CanEditMultipleObjects]
	public class SkinAttachmentEditor : Editor
	{
		private Editor attachmentTargetEditor;
		private HashSet<SkinAttachmentTarget> attachmentTargetSet = new HashSet<SkinAttachmentTarget>();

		public override void OnInspectorGUI()
		{
			attachmentTargetSet.Clear();

			if (target == null)
				return;

			if (targets.Length == 1)
			{
				var attachment = target as SkinAttachment;
				if (attachment == null)
					return;

				EditorGUILayout.HelpBox(attachment.attached ? "Currently attached to " + attachment.target : "Currently detached.", MessageType.Info);
				DrawGUIAttachDetach(attachment, attachmentTargetSet);
				CommitTargetChanges(attachmentTargetSet);
				EditorGUILayout.Separator();

				base.OnInspectorGUI();

				var attachmentTarget = (target as SkinAttachment).target;
				if (attachmentTarget != null)
				{
					EditorGUILayout.Separator();
					Editor.CreateCachedEditor(attachmentTarget, null, ref attachmentTargetEditor);
					attachmentTargetEditor.DrawHeader();
					attachmentTargetEditor.OnInspectorGUI();
				}
			}
			else
			{
				EditorGUILayout.HelpBox("Multiple attachments selected.", MessageType.Warning);

				foreach (var target in targets)
				{
					var attachment = target as SkinAttachment;
					if (attachment == null)
						continue;

					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.ObjectField(attachment, typeof(SkinAttachment), false);
					DrawGUIAttachDetach(attachment, attachmentTargetSet);
					CommitTargetChanges(attachmentTargetSet);
					EditorGUILayout.EndHorizontal();
				}

				EditorGUILayout.Separator();
				EditorGUILayout.BeginVertical();
				DrawGUIAttachDetachAll(targets, attachmentTargetSet);
				CommitTargetChanges(attachmentTargetSet);
				EditorGUILayout.EndVertical();
			}
		}

		public static void CommitTargetChanges(HashSet<SkinAttachmentTarget> attachmentTargetSet)
		{
			foreach (var attachmentTarget in attachmentTargetSet)
			{
				if (attachmentTarget != null)
				{
					attachmentTarget.CommitSubjectsIfRequired();
					EditorUtility.SetDirty(attachmentTarget);
				}
			}
		}

		public static void DrawGUIAttachDetach(SkinAttachment attachment, HashSet<SkinAttachmentTarget> attachmentTargetSet)
		{
			EditorGUILayout.BeginVertical();
			DrawGUIAttach(attachment, attachmentTargetSet);
			DrawGUIDetach(attachment, attachmentTargetSet);
			EditorGUILayout.EndVertical();
		}

		public static void DrawGUIAttach(SkinAttachment attachment, HashSet<SkinAttachmentTarget> attachmentTargetSet)
		{
			EditorGUI.BeginDisabledGroup(attachment.attached);
			{
				if (GUILayout.Button("Attach"))
				{
					attachment.Attach(storePositionRotation: true);
					attachmentTargetSet.Add(attachment.targetActive);
					EditorUtility.SetDirty(attachment);
				}
			}
			EditorGUI.EndDisabledGroup();
		}

		public static void DrawGUIDetach(SkinAttachment attachment, HashSet<SkinAttachmentTarget> attachmentTargetSet)
		{
			EditorGUI.BeginDisabledGroup(!attachment.attached);
			{
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Detach"))
				{
					attachmentTargetSet.Add(attachment.targetActive);
					attachment.Detach(revertPositionRotation: true);
					EditorUtility.SetDirty(attachment);
				}
				if (GUILayout.Button("+ Hold", GUILayout.ExpandWidth(false)))
				{
					attachmentTargetSet.Add(attachment.targetActive);
					attachment.Detach(revertPositionRotation: false);
					EditorUtility.SetDirty(attachment);
				}
				EditorGUILayout.EndHorizontal();
			}
			EditorGUI.EndDisabledGroup();
		}

		public static void DrawGUIAttachDetachAll(Object[] targets, HashSet<SkinAttachmentTarget> attachmentTargetSet)
		{
			if (GUILayout.Button("Attach all"))
			{
				foreach (var target in targets)
				{
					var attachment = target as SkinAttachment;
					if (attachment != null && !attachment.attached)
					{
						attachment.Attach(storePositionRotation: true);
						attachmentTargetSet.Add(attachment.targetActive);
						EditorUtility.SetDirty(attachment);
					}
				}
			}
			if (GUILayout.Button("Detach all"))
			{
				foreach (var target in targets)
				{
					var attachment = target as SkinAttachment;
					if (attachment != null && attachment.attached)
					{
						attachmentTargetSet.Add(attachment.targetActive);
						attachment.Detach(revertPositionRotation: true);
						EditorUtility.SetDirty(attachment);
					}
				}
			}
		}
	}
}
