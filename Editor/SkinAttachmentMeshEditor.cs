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
        private bool settingsToggled = false;
        private bool debugToggled = false;
        private bool storageToggled = true;

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
                    DrawGUIAttachmentDataStorage(attachment);
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
                DrawGUIAttachmentDataStorage(attachment);
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

        public void DrawGUIAttachmentDataStorage(SkinAttachmentMesh attachment)
        {
            SkinAttachmentEditorUtils.DrawGUIAttachmentDataStorage(attachment, attachment.common);
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
                SkinAttachmentEditorUtils.DrawGUIAttachmentDataStorage(a, a.common);
                DrawGUIAttachmentTarget(a);
                DrawGUIAttach(a);
                DrawGUIDetach(a);
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}