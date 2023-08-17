using System.Linq;
using Unity.DemoTeam.DigitalHuman;
using UnityEditor;
using UnityEngine;

public static class SkinAttachmentEditorUtils 
{
    public static void DrawGUIAttachmentDataStorage(MonoBehaviour attachment, SkinAttachmentComponentCommon common)
    {
        common.dataStorage =
            (SkinAttachmentDataRegistry)EditorGUILayout.ObjectField(common.dataStorage,
                typeof(SkinAttachmentDataRegistry));
        if (common.dataStorage == null)
        {
            EditorGUILayout.HelpBox("SkinAttachmentDataStorage needs to be assigned before attaching!",
                MessageType.Error);
        }
        else
        {
            common.poseDataSource =
                (SkinAttachmentComponentCommon.PoseDataSource)EditorGUILayout.EnumPopup("Pose Data Source:",
                    common.poseDataSource);
            
            if (common.poseDataSource == SkinAttachmentComponentCommon.PoseDataSource.BuildPoses)
            {
                if (GUILayout.Button("Rebake"))
                {
                    common.BakeAttachmentDataToSceneOrPrefab(attachment);
                }
            }
            else
            {
                //TODO: remember the last index and maintain the list if this gets too heavy
                Hash128 currentHash = common.linkedChecksum;
                var hashList = common.dataStorage.GetAllEntries().Select(o => o.hashKey).ToList();
                int index = hashList.FindIndex(o => o.Equals(currentHash));
                string[] options = hashList.Select(o => o.ToString()).ToArray();
                
                index = EditorGUILayout.Popup("Linked Attachment Data", index, options);
                if (index != -1)
                {
                    common.linkedChecksum = hashList[index];
                }
                
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
