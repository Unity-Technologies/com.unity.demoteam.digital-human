using System.Linq;
using Unity.DemoTeam.DigitalHuman;
using UnityEditor;
using UnityEngine;

public static class SkinAttachmentEditorUtils 
{
    public static void DrawGUIAttachmentDataStorage(SerializedProperty common, bool drawPoseDataSource = true)
    {
        EditorGUI.BeginDisabledGroup(common.isInstantiatedPrefab);
        
        SerializedProperty dataStorage = common.FindPropertyRelative("dataStorage");
        SerializedProperty poseDataSource = common.FindPropertyRelative("poseDataSource");
        SerializedProperty linkedChecksum = common.FindPropertyRelative("linkedChecksum");

        EditorGUILayout.ObjectField(dataStorage, typeof(SkinAttachmentDataRegistry));
        if (dataStorage == null)
        {
            EditorGUILayout.HelpBox("SkinAttachmentDataStorage needs to be assigned before attaching!",
                MessageType.Error);
        }
        else
        {
            if (drawPoseDataSource)
            {
                EditorGUILayout.PropertyField(poseDataSource, new GUIContent("Pose Data Source:"), false);

                if (poseDataSource.intValue == (int)SkinAttachmentComponentCommon.PoseDataSource.BuildPoses)
                {
                    if (GUILayout.Button("Rebake"))
                    {
                        common.serializedObject.ApplyModifiedProperties();
                        Object[] targets = common.serializedObject.targetObjects;
                        foreach (var target in targets)
                        {
                            SkinAttachmentComponentCommon.ISkinAttachmentComponent sac = (SkinAttachmentComponentCommon.ISkinAttachmentComponent)target;
                            if (sac != null && sac is MonoBehaviour)
                            {
                                sac.GetCommonComponent().BakeAttachmentDataToSceneOrPrefab((MonoBehaviour)sac);
                            }
                        }
                        common.serializedObject.Update();
                    }
                }
                else
                {
                    //TODO: remember the last index and maintain the list if this gets too heavy
                    Hash128 currentHash = linkedChecksum.hash128Value;
                    var hashList = ((SkinAttachmentDataRegistry)dataStorage.objectReferenceValue).GetAllEntries().Select(o => o.hashKey).ToList();
                    int index = hashList.FindIndex(o => o.Equals(currentHash));
                    string[] options = hashList.Select(o => o.ToString()).ToArray();

                    index = EditorGUILayout.Popup("Linked Attachment Data", index, options);
                    if (index != -1)
                    {
                        linkedChecksum.hash128Value = hashList[index];
                    }

                }
            }
        }
        EditorGUI.EndDisabledGroup();
    }
    
    public static void DrawGUIAttachmentTarget(SerializedProperty common)
    {
        EditorGUI.BeginDisabledGroup(common.isInstantiatedPrefab);
        
        SerializedProperty at = common.FindPropertyRelative("attachmentTarget");
        var hash = at.contentHash;
        EditorGUILayout.ObjectField(at, typeof(Renderer));
        
        EditorGUI.EndDisabledGroup();
    }

    public static void DrawGUISettings(SerializedProperty common)
    {
        SerializedProperty sm = common.FindPropertyRelative("schedulingMode");
        SerializedProperty es = common.FindPropertyRelative("explicitScheduling");
        SerializedProperty ebm = common.FindPropertyRelative("explicitBakeMesh");
        
        EditorGUILayout.PropertyField(sm, new GUIContent("Scheduling: "), false);
        EditorGUILayout.PropertyField(es, new GUIContent("Explicit Scheduling: "), false);
        
        EditorGUI.BeginDisabledGroup(common.isInstantiatedPrefab);
        EditorGUILayout.PropertyField(ebm, new GUIContent("explicit mesh for baking (optional):"), false);
        EditorGUI.EndDisabledGroup();
    }
    
    
    public static void DrawGuiDebug(SerializedProperty common)
    {
        SerializedProperty showAttachmentTargetForBaking = common.FindPropertyRelative("showAttachmentTargetForBaking");
        EditorGUILayout.PropertyField(showAttachmentTargetForBaking, new GUIContent("Draw Bake target: "), false);
    }
}
