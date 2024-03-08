using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace Unity.DemoTeam.DigitalHuman
{
    
    [CustomEditor(typeof(SkinAttachmentMesh)), CanEditMultipleObjects]
    public class SkinAttachmentMeshEditor : Editor
    {
        private static bool settingsToggled = false;
		private static bool debugToggled = false;
		private static bool storageToggled = true;

		private SerializedProperty serializedCommon;

		public override void OnInspectorGUI()
		{
			if (target == null)
				return;

			serializedCommon = serializedObject.FindProperty("common");

			if (targets.Length == 1)
			{
				var attachment = target as SkinAttachmentMesh;
				if (attachment == null)
					return;
				
				if (attachment.common.dataStorage != null)
				{
					EditorGUILayout.HelpBox(attachment.IsAttached ? "Currently attached to " + attachment.common.attachmentTarget + "\nData storage hash: " + attachment.common.CheckSum : "Currently detached.", MessageType.Info);
				}
				
				DrawGUIAttachDetach(attachment);
			}
			else
			{
				IEnumerable<SkinAttachmentMesh> transforms = targets.Where(o => o is SkinAttachmentMesh).Cast<SkinAttachmentMesh>();
				DrawMultiSelectGUI(transforms);
			}

			DrawGUIAttachmentTarget(serializedCommon);
			DrawGUIAttachmentDataStorage(serializedCommon);
			DrawGuiSettings(serializedObject, serializedCommon);
			DrawGuiDebug(serializedObject, serializedCommon);

			serializedObject.ApplyModifiedProperties();
		}

		
		public void DrawGUIAttachmentTarget(SerializedProperty common)
		{
			SkinAttachmentEditorUtils.DrawGUIAttachmentTarget(common);
		}
		
		public void DrawGUIAttachmentDataStorage(SerializedProperty common)
		{
			storageToggled = EditorGUILayout.BeginFoldoutHeaderGroup(storageToggled, "Storage");
			if (storageToggled)
			{
				EditorGUILayout.BeginVertical();
				SkinAttachmentEditorUtils.DrawGUIAttachmentDataStorage(common);
				EditorGUILayout.EndVertical();
			}

			EditorGUILayout.EndFoldoutHeaderGroup();
		}

		public void DrawGuiSettings(SerializedObject so, SerializedProperty common)
		{
			settingsToggled = EditorGUILayout.BeginFoldoutHeaderGroup(settingsToggled, "Settings");
			if (settingsToggled)
			{
				EditorGUILayout.BeginVertical();
				EditorGUILayout.PropertyField(so.FindProperty("attachmentType"), new GUIContent("AttachmentType: "));
				SkinAttachmentEditorUtils.DrawGUISettings(common);
				EditorGUILayout.EndVertical();
				
			}
			EditorGUILayout.EndFoldoutHeaderGroup();
		}
		
		public void DrawGuiDebug(SerializedObject attachment,SerializedProperty common )
		{
			debugToggled = EditorGUILayout.BeginFoldoutHeaderGroup(debugToggled, "Debug");
			if (debugToggled)
			{
				EditorGUILayout.BeginVertical();
				SkinAttachmentEditorUtils.DrawGuiDebug(common);
				EditorGUILayout.EndVertical();
				
			}
			EditorGUILayout.EndFoldoutHeaderGroup();
		}
		
		public void DrawGUIAttachDetach(SkinAttachmentMesh attachment)
        {
        	EditorGUILayout.BeginHorizontal();
        	DrawGUIAttach(attachment);
        	DrawGUIDetach(attachment);
        	EditorGUILayout.EndHorizontal();
        }

		public void DrawGUIAttach(SkinAttachmentMesh attachment)
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

		public void DrawGUIDetach(SkinAttachmentMesh attachment)
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

		public void DrawMultiSelectGUI(IEnumerable<SkinAttachmentMesh> transforms)
		{
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Attach All"))
			{
				foreach (var t in transforms)
				{
					t.Attach();
				}
				serializedObject.Update();
			}
			
			if (GUILayout.Button("Detach All"))
			{
				foreach (var t in transforms)
				{
					t.Detach();
				}
				serializedObject.Update();
			}
			GUILayout.EndHorizontal();
		}

		public void DrawValidationInfo(SkinAttachmentMesh attachment)
		{
			if (!attachment.ValidateBakedData())
			{
				EditorGUILayout.HelpBox("Baked data is invalid, rebake needed" , MessageType.Error);
			}
		}
		
    }
}