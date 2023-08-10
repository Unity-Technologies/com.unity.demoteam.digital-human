using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SkinAttachmentTransform))]
	public class SkinAttachmentTransformEditor : Editor
	{
		private bool settingsToggled = false;
		private bool debugToggled = false;
		public override void OnInspectorGUI()
		{
			if (target == null)
				return;

			var attachment = target as SkinAttachmentTransform;
			if (attachment == null)
				return;

			//we always need data storage before anything else
			if (attachment.common.dataStorage == null)
			{
				DrawGUIAttachmentDataStorage(attachment);
			}
			else
			{
				EditorGUILayout.HelpBox(attachment.IsAttached ? "Currently attached to " + attachment.common.attachmentTarget + "\nData storage hash: " + attachment.common.CheckSum : "Currently detached.", MessageType.Info);
				DrawGUIAttachDetach(attachment);
				DrawGUIAttachmentDataStorage(attachment);
				DrawGUIAttachmentTarget(attachment);
				DrawGuiSettings(attachment);
				DrawGuiDebug(attachment);
				
			}
		}

		
		public void DrawGUIAttachmentTarget(SkinAttachmentTransform attachment)
		{
			var oldAttachment = attachment.common.attachmentTarget;
			attachment.common.attachmentTarget = (Renderer)EditorGUILayout.ObjectField(attachment.common.attachmentTarget, typeof(Renderer));
			if (oldAttachment != attachment.common.attachmentTarget && oldAttachment != null)
			{
				attachment.Detach(false);
			}
		}
		
		public void DrawGUIAttachmentDataStorage(SkinAttachmentTransform attachment)
		{
			SkinAttachmentEditorUtils.DrawGUISettings(attachment, attachment.common);
		}

		public void DrawGuiSettings(SkinAttachmentTransform attachment)
		{
			settingsToggled = EditorGUILayout.BeginFoldoutHeaderGroup(settingsToggled, "Settings");
			if (settingsToggled)
			{
				EditorGUILayout.BeginVertical();
				SkinAttachmentEditorUtils.DrawGUISettings(attachment, attachment.common);
				EditorGUILayout.EndVertical();
				
			}
			EditorGUILayout.EndFoldoutHeaderGroup();
		}
		
		public void DrawGuiDebug(SkinAttachmentTransform attachment)
		{
			debugToggled = EditorGUILayout.BeginFoldoutHeaderGroup(debugToggled, "Debug");
			if (debugToggled)
			{
				EditorGUILayout.BeginVertical();
				attachment.readbackTransformFromGPU = EditorGUILayout.Toggle("Readback positions from GPU: ", attachment.readbackTransformFromGPU);
				SkinAttachmentEditorUtils.DrawGuiDebug(attachment, attachment.common);
				EditorGUILayout.EndVertical();
				
			}
			EditorGUILayout.EndFoldoutHeaderGroup();
		}
		
		public void DrawGUIAttachDetach(SkinAttachmentTransform attachment)
        {
        	EditorGUILayout.BeginHorizontal();
        	DrawGUIAttach(attachment);
        	DrawGUIDetach(attachment);
        	EditorGUILayout.EndHorizontal();
        }

		public void DrawGUIAttach(SkinAttachmentTransform attachment)
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

		public void DrawGUIDetach(SkinAttachmentTransform attachment)
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