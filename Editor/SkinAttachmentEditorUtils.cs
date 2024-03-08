using System.Linq;
using Unity.DemoTeam.DigitalHuman;
using UnityEditor;
using UnityEngine;

public static class SkinAttachmentEditorUtils 
{
    public static void DrawGUIAttachmentDataStorage(SerializedProperty common, bool drawPoseDataSource = true)
    {
        SerializedProperty dataStorage = common.FindPropertyRelative("dataStorage");
        SerializedProperty poseDataSource = common.FindPropertyRelative("poseDataSource");
        SerializedProperty linkedChecksum = common.FindPropertyRelative("linkedChecksum");
        bool isAttached = common.FindPropertyRelative("attached").boolValue;

        if (dataStorage.objectReferenceValue == null && !dataStorage.serializedObject.isEditingMultipleObjects)
        {
            EditorGUILayout.HelpBox("If not datastorage is explicitly defined, the registry will default to: " + SkinAttachmentDataRegistry.GetDefaultRegistryPath(common.serializedObject.targetObject), MessageType.Info);
            
        }
        EditorGUILayout.ObjectField(dataStorage, typeof(SkinAttachmentDataRegistry));
        
        if (drawPoseDataSource)
        {
            EditorGUILayout.PropertyField(poseDataSource, new GUIContent("Pose Data Source:"), false);

            if (poseDataSource.intValue == (int)SkinAttachmentComponentCommon.PoseDataSource.BuildPoses)
            {
                EditorGUI.BeginDisabledGroup(!isAttached);
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
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                //TODO: remember the last index and maintain the list if this gets too heavy
                var registry = (SkinAttachmentDataRegistry)dataStorage.objectReferenceValue;
                if (registry != null)
                {
                    Hash128 currentHash = linkedChecksum.hash128Value;
                    var hashList = registry.GetAllEntries().Select(o => o.hashKey).ToList();
                    int index = hashList.FindIndex(o => o.Equals(currentHash));
                    string[] options = hashList.Select(o => o.ToString()).ToArray();

                    index = EditorGUILayout.Popup("Linked Attachment Data", index, options);
                    if (index != -1)
                    {
                        linkedChecksum.hash128Value = hashList[index];
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Assign Skin Attachment registry to select linked entry!",
                        MessageType.Warning);
                }
                

            }
        }
        

    }
    
    public static void DrawGUIAttachmentTarget(SerializedProperty common)
    {
        SerializedProperty at = common.FindPropertyRelative("attachmentTarget");
        var hash = at.contentHash;
        EditorGUILayout.ObjectField(at, typeof(Renderer));
    }

    public static void DrawGUISettings(SerializedProperty common)
    {
        SerializedProperty sm = common.FindPropertyRelative("schedulingMode");
        SerializedProperty es = common.FindPropertyRelative("explicitScheduling");
        SerializedProperty ebm = common.FindPropertyRelative("explicitBakeMesh");
        SerializedProperty rtm = common.FindPropertyRelative("readbackTargetMeshWhenBaking");
        
        EditorGUILayout.PropertyField(sm, new GUIContent("Scheduling: "), false);
        EditorGUILayout.PropertyField(es, new GUIContent("Explicit Scheduling: "), false);
        EditorGUILayout.PropertyField(ebm, new GUIContent("Explicit mesh for baking (optional):"), false);
        EditorGUILayout.PropertyField(rtm, new GUIContent("Readback target mesh from GPU when baking: "), false);
    }
    
    
    public static void DrawGuiDebug(SerializedProperty common)
    {
        SerializedProperty showAttachmentTargetForBaking = common.FindPropertyRelative("showAttachmentTargetForBaking");
        EditorGUILayout.PropertyField(showAttachmentTargetForBaking, new GUIContent("Draw Bake target: "), false);
    }
}
