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

			//we always need data storage before anything else
			if (attachment.common.dataStorage == null)
			{
				DrawGUIAttachmentDataStorage(attachment);
			}
			else
			{
				EditorGUILayout.HelpBox(attachment.IsAttached ? "Currently attached to " + attachment.common.attachmentTarget : "Currently detached.", MessageType.Info);
				DrawGUIAttachDetach(attachment);
				EditorGUILayout.Separator();
				DrawGUIAttachmentDataStorage(attachment);
				EditorGUILayout.Separator();
				DrawGUIAttachmentTarget(attachment);
				DrawGuiSettings(attachment);
			}
		}

		
		public static void DrawGUIAttachmentTarget(SkinAttachmentMesh attachment)
		{
			var oldAttachment = attachment.common.attachmentTarget;
			attachment.common.attachmentTarget = (Renderer)EditorGUILayout.ObjectField(attachment.common.attachmentTarget, typeof(Renderer));
			if (oldAttachment != attachment.common.attachmentTarget && oldAttachment != null)
			{
				attachment.Detach(false);
			}
		}
		
		public static void DrawGUIAttachmentDataStorage(SkinAttachmentMesh attachment)
		{
			attachment.common.dataStorage = (SkinAttachmentDataStorage)EditorGUILayout.ObjectField(attachment.common.dataStorage, typeof(SkinAttachmentDataStorage));
			if (attachment.common.dataStorage == null)
			{
				EditorGUILayout.HelpBox("SkinAttachmentDataStorage needs to be assigned before attaching!", MessageType.Error);
			}
		}

		public static void DrawGuiSettings(SkinAttachmentMesh attachment)
		{
			EditorGUILayout.BeginVertical();
			attachment.attachmentType = (SkinAttachmentMesh.MeshAttachmentType)EditorGUILayout.EnumPopup("AttachmentType: ", attachment.attachmentType);
			attachment.common.schedulingMode = (SkinAttachmentComponentCommon.SchedulingMode)EditorGUILayout.EnumPopup("Scheduling: ", attachment.common.schedulingMode);
			EditorGUILayout.EndVertical();
		}
		
		public static void DrawGUIAttachDetach(SkinAttachmentMesh attachment)
        		{
        			EditorGUILayout.BeginHorizontal();
        			DrawGUIAttach(attachment);
        			DrawGUIDetach(attachment);
        			EditorGUILayout.EndHorizontal();
        		}

		public static void DrawGUIAttach(SkinAttachmentMesh attachment)
		{
			EditorGUI.BeginDisabledGroup(!attachment.CanAttach());
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
