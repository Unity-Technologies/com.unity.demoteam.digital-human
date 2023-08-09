using System.Collections;
using System.Collections.Generic;
using Unity.DemoTeam.DigitalHuman;
using UnityEditor;
using UnityEngine;

public static class SkinAttachmentEditorUtils 
{
    public static void DrawGUIAttachmentDataStorage(MonoBehaviour attachment, SkinAttachmentComponentCommon common)
    {
        common.dataStorage =
            (SkinAttachmentDataStorage)EditorGUILayout.ObjectField(common.dataStorage,
                typeof(SkinAttachmentDataStorage));
        if (common.dataStorage == null)
        {
            EditorGUILayout.HelpBox("SkinAttachmentDataStorage needs to be assigned before attaching!",
                MessageType.Error);
        }
        else
        {
            if (GUILayout.Button("Rebake"))
            {
                common.BakeAttachmentDataToSceneOrPrefab(attachment);
            }
        }
    }

    public static void DrawGUISettings(MonoBehaviour attachment, SkinAttachmentComponentCommon common)
    {
        common.schedulingMode = (SkinAttachmentComponentCommon.SchedulingMode)EditorGUILayout.EnumPopup("Scheduling: ", common.schedulingMode);
        common.explicitScheduling = EditorGUILayout.Toggle("Explicit Scheduling: ", common.explicitScheduling);
        common.explicitBakeMesh = (Mesh)EditorGUILayout.ObjectField("explicit mesh for baking (optional):", common.explicitBakeMesh, typeof(Mesh), false);
    }
    
    
    public static void DrawGuiDebug(MonoBehaviour attachment, SkinAttachmentComponentCommon common)
    {
        common.showAttachmentTargetForBaking = EditorGUILayout.Toggle("Draw Bake target: ", common.showAttachmentTargetForBaking);
    }
}
