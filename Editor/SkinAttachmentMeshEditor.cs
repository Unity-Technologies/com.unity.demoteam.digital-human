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
        
        private SkinAttachmentDataRegistry prototypeRegistry = null;
        private Renderer prototypeRenderer = null;
        private bool prototypeScheduleExplicitly = false;
        private SkinAttachmentComponentCommon.SchedulingMode prototypeSchedlingMode = SkinAttachmentComponentCommon.SchedulingMode.GPU;
        private Mesh prototypeMesh = null;
        private SkinAttachmentMesh.MeshAttachmentType prototypeAttachmentMesh = SkinAttachmentMesh.MeshAttachmentType.Mesh;
        private bool onlyAllowOneRootPrototype = false;
        private bool prototypeGeneratePrecalculatedMotionVectors = true;

        public override void OnInspectorGUI()
        {
            if (target == null)
                return;


            if (targets.Length == 1)
            {
                var attachment = target as SkinAttachmentMesh;
                if (attachment == null)
                    return;

                //we always need data storage before anything else
                if (attachment.common.dataStorage == null)
                {
                    DrawGUIStorage(attachment);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        attachment.IsAttached
                            ? "Currently attached to " + attachment.common.attachmentTarget + "\nData storage hash: " +
                              attachment.common.CheckSum
                            : "Currently detached.", MessageType.Info);
                    DrawValidationInfo(attachment);
                    DrawGUIAttachmentTarget(attachment);
                    DrawGUIAttachDetach(attachment);
                    DrawGUIStorage(attachment);
                    DrawGuiSettings(attachment);
                    DrawGuiDebug(attachment);
                }
            }
            else
            {
                IEnumerable<SkinAttachmentMesh> attachments = targets.Where(o => o is SkinAttachmentMesh).Cast<SkinAttachmentMesh>();
                DrawMultiSelectGUI(attachments);
            }
        }

        public void DrawGUIStorage(SkinAttachmentMesh attachment)
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

        public static void DrawGUIAttachmentTarget(SkinAttachmentMesh attachment)
        {
            var oldAttachment = attachment.common.attachmentTarget;
            attachment.common.attachmentTarget =
                (Renderer)EditorGUILayout.ObjectField(attachment.common.attachmentTarget, typeof(Renderer));
            if (oldAttachment != attachment.common.attachmentTarget && oldAttachment != null)
            {
                attachment.Detach(false);
            }
        }


        public void DrawGuiSettings(SkinAttachmentMesh attachment)
        {
            settingsToggled = EditorGUILayout.BeginFoldoutHeaderGroup(settingsToggled, "Settings");
            if (settingsToggled)
            {
                EditorGUILayout.BeginVertical();
                attachment.attachmentType = (SkinAttachmentMesh.MeshAttachmentType)EditorGUILayout.EnumPopup("AttachmentType: ", attachment.attachmentType);
                SkinAttachmentEditorUtils.DrawGUISettings(attachment, attachment.common);
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
        
        public void DrawGuiDebug(SkinAttachmentMesh attachment)
        {
            debugToggled = EditorGUILayout.BeginFoldoutHeaderGroup(debugToggled, "Debug");
            if (debugToggled)
            {
                EditorGUILayout.BeginVertical();
                SkinAttachmentEditorUtils.DrawGuiDebug(attachment, attachment.common);
                EditorGUILayout.EndVertical();
				
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        public void DrawValidationInfo(SkinAttachmentMesh attachment)
        {
            if (!attachment.IsAttachmentMeshValid())
            {
                if (attachment.meshAsset == null)
                {
                    EditorGUILayout.HelpBox("SkinAttachmentMesh is null!", MessageType.Error);
                }
                else
                {
                    if (!attachment.meshAsset.isReadable)
                    {
                        EditorGUILayout.HelpBox("SkinAttachmentMesh is not readable! Enable read/write from asset settings" , MessageType.Error);
                    }
                }
            }

            if (!attachment.ValidateBakedData())
            {
                EditorGUILayout.HelpBox("Baked data is invalid, rebake needed" , MessageType.Error);
            }
        }
        
        public void DrawMultiSelectGUI(IEnumerable<SkinAttachmentMesh> attachments)
        {
            foreach (var a in attachments)
            {
                EditorGUILayout.BeginHorizontal();
                DrawGUIAttachmentTarget(a);
                SkinAttachmentEditorUtils.DrawGUIAttachmentDataStorage(a, a.common, false);
                DrawGUIAttach(a);
                DrawGUIDetach(a);
                EditorGUILayout.EndHorizontal();
            }
            
            if (GUILayout.Button("Attach All"))
            {
                foreach (var t in attachments)
                {
                    t.Attach();
                }
            }
			
            if (GUILayout.Button("Detach All"))
            {
                foreach (var t in attachments)
                {
                    t.Detach();
                }
            }
            
            if (GUILayout.Button("Copy Settings From First In List"))
            {
                var prototype = attachments.Last();
                foreach (var t in attachments)
                {
                    t.attachmentType = prototype.attachmentType;
                    t.DataStorage = prototypeRegistry;
                    t.Target = prototypeRenderer;
                    t.ScheduleExplicitly = prototypeScheduleExplicitly;
                    t.SchedulingMode = prototypeSchedlingMode;
                    t.allowOnlyOneRoot = prototype.allowOnlyOneRoot;
                    t.generatePrecalculatedMotionVectors = prototype.generatePrecalculatedMotionVectors;
                    t.ExplicitTargetBakeMesh = prototypeMesh;
                }
            }

            DrawPrototypeEntries();
        }
        
        public void DrawPrototypeEntries()
        {
            prototypeRegistry =
                (SkinAttachmentDataRegistry)EditorGUILayout.ObjectField(prototypeRegistry,
                    typeof(SkinAttachmentDataRegistry));
			
            prototypeRenderer = (Renderer)EditorGUILayout.ObjectField(prototypeRenderer, typeof(Renderer));
            prototypeSchedlingMode = (SkinAttachmentComponentCommon.SchedulingMode)EditorGUILayout.EnumPopup("Scheduling: ", prototypeSchedlingMode);
            prototypeScheduleExplicitly = EditorGUILayout.Toggle("Explicit Scheduling: ", prototypeScheduleExplicitly);
            prototypeMesh = (Mesh)EditorGUILayout.ObjectField("explicit mesh for baking (optional):", prototypeMesh, typeof(Mesh), false);
            prototypeAttachmentMesh = (SkinAttachmentMesh.MeshAttachmentType)EditorGUILayout.EnumPopup("AttachmentType: ", prototypeAttachmentMesh);
            onlyAllowOneRootPrototype = EditorGUILayout.Toggle("Only Allow One Root (MeshRoots): ", onlyAllowOneRootPrototype);
            prototypeGeneratePrecalculatedMotionVectors = EditorGUILayout.Toggle("Generate Precalculated Motion Vectors: ", prototypeGeneratePrecalculatedMotionVectors);
			
        }
    }
}