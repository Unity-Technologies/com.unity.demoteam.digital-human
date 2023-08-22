using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SkinAttachmentTransform)), CanEditMultipleObjects]
	public class SkinAttachmentTransformEditor : Editor
	{
		private bool settingsToggled = false;
		private bool debugToggled = false;
		private bool storageToggled = true;

		private SkinAttachmentDataRegistry prototypeRegistry = null;
		private Renderer prototypeRenderer = null;
		private bool prototypeScheduleExplicitly = false;
		private SkinAttachmentComponentCommon.SchedulingMode prototypeSchedlingMode = SkinAttachmentComponentCommon.SchedulingMode.GPU;
		private Mesh prototypeMesh = null;
		
		public override void OnInspectorGUI()
		{
			if (target == null)
				return;

			if (targets.Length == 1)
			{
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
					DrawValidationInfo(attachment);
					DrawGUIAttachmentTarget(attachment);
					DrawGUIAttachDetach(attachment);
					DrawGUIAttachmentDataStorage(attachment);
					
					DrawGuiSettings(attachment);
					DrawGuiDebug(attachment);
				
				}
			}
			else
			{
				IEnumerable<SkinAttachmentTransform> transforms = targets.Where(o => o is SkinAttachmentTransform).Cast<SkinAttachmentTransform>();
				
				DrawMultiSelectGUI(transforms);
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
			storageToggled = EditorGUILayout.BeginFoldoutHeaderGroup(storageToggled, "Storage");
			if (storageToggled)
			{
				EditorGUILayout.BeginVertical();
				SkinAttachmentEditorUtils.DrawGUIAttachmentDataStorage(attachment, attachment.common);
				EditorGUILayout.EndVertical();
			}

			EditorGUILayout.EndFoldoutHeaderGroup();
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

		public void DrawMultiSelectGUI(IEnumerable<SkinAttachmentTransform> transforms)
		{

			foreach (var t in transforms)
			{
				EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
				DrawGUIAttachmentTarget(t);
				SkinAttachmentEditorUtils.DrawGUIAttachmentDataStorage(t, t.common, false);
				DrawGUIAttach(t);
				DrawGUIDetach(t);
				EditorGUILayout.EndHorizontal();
			}

			if (GUILayout.Button("Attach All"))
			{
				foreach (var t in transforms)
				{
					t.Attach();
				}
			}
			
			if (GUILayout.Button("Detach All"))
			{
				foreach (var t in transforms)
				{
					t.Detach();
				}
			}
			
			EditorGUILayout.BeginVertical(new GUIStyle(EditorStyles.helpBox));
			EditorGUILayout.LabelField("Prototype settings");
			DrawPrototypeEntries();
			if (GUILayout.Button("Apply To All"))
			{
				foreach (var t in transforms)
				{
					t.DataStorage = prototypeRegistry;
					t.Target = prototypeRenderer;
					t.ScheduleExplicitly = prototypeScheduleExplicitly;
					t.SchedulingMode = prototypeSchedlingMode;
					t.ExplicitTargetBakeMesh = prototypeMesh;
				}
			}
			EditorGUILayout.EndVertical();
		}

		public void DrawPrototypeEntries()
		{
			EditorGUILayout.BeginHorizontal();
			prototypeRegistry =
				(SkinAttachmentDataRegistry)EditorGUILayout.ObjectField(prototypeRegistry,
					typeof(SkinAttachmentDataRegistry));
			
			prototypeRenderer = (Renderer)EditorGUILayout.ObjectField(prototypeRenderer, typeof(Renderer));
			EditorGUILayout.EndHorizontal();
			
			prototypeSchedlingMode = (SkinAttachmentComponentCommon.SchedulingMode)EditorGUILayout.EnumPopup("Scheduling: ", prototypeSchedlingMode);
			prototypeScheduleExplicitly = EditorGUILayout.Toggle("Explicit Scheduling: ", prototypeScheduleExplicitly);
			prototypeMesh = (Mesh)EditorGUILayout.ObjectField("Explicit mesh for baking (optional):", prototypeMesh, typeof(Mesh), false);
			
		}
		
		public void DrawValidationInfo(SkinAttachmentTransform attachment)
		{
			if (!attachment.ValidateBakedData())
			{
				EditorGUILayout.HelpBox("Baked data is invalid, rebake needed" , MessageType.Error);
			}
		}

	}
}
