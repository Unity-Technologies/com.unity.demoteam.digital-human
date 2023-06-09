using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SkinAttachmentMesh))]
	public class SkinAttachmentMeshEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			if (target == null)
				return;

			var attachment = target as SkinAttachmentMesh;
			if (attachment == null)
				return;

			EditorGUILayout.HelpBox(attachment.IsAttached ? "Currently attached to " + attachment.attachmentTarget : "Currently detached.", MessageType.Info);
			DrawGUIAttachDetach(attachment);
			EditorGUILayout.Separator();
			DrawGUIAttachmentTarget(attachment);
			DrawGuiSettings(attachment);


		}

		
		public static void DrawGUIAttachmentTarget(SkinAttachmentMesh attachment)
		{
			var oldAttachment = attachment.attachmentTarget;
			attachment.attachmentTarget = (Renderer)EditorGUILayout.ObjectField(attachment.attachmentTarget, typeof(Renderer));
			if (oldAttachment != attachment.attachmentTarget && oldAttachment != null)
			{
				attachment.Detach(false);
			}
		}

		public static void DrawGUIAttachDetach(SkinAttachmentMesh attachment)
		{
			EditorGUILayout.BeginHorizontal();
			DrawGUIAttach(attachment);
			DrawGUIDetach(attachment);
			EditorGUILayout.EndHorizontal();
		}
		
		public static void DrawGuiSettings(SkinAttachmentMesh attachment)
		{
			EditorGUILayout.BeginVertical();
			attachment.attachmentType = (SkinAttachmentMesh.MeshAttachmentType)EditorGUILayout.EnumPopup("AttachmentType: ", attachment.attachmentType);
			attachment.schedulingMode = (SkinAttachmentMesh.SchedulingMode)EditorGUILayout.EnumPopup("Scheduling: ", attachment.schedulingMode);
			EditorGUILayout.EndVertical();
		}

		public static void DrawGUIAttach(SkinAttachmentMesh attachment)
		{
			EditorGUI.BeginDisabledGroup(attachment.IsAttached);
			{
				if (GUILayout.Button("Attach"))
				{
					attachment.Attach(storePositionRotation: true);
					EditorUtility.SetDirty(attachment);
				}
			}
			EditorGUI.EndDisabledGroup();
		}

		public static void DrawGUIDetach(SkinAttachmentMesh attachment)
		{
			EditorGUI.BeginDisabledGroup(!attachment.IsAttached);
			{
				if (GUILayout.Button("Detach"))
				{
					attachment.Detach(revertPositionRotation: true);
					EditorUtility.SetDirty(attachment);
				}
			}
			EditorGUI.EndDisabledGroup();
		}

	}
}
