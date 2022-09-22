using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Unity.DemoTeam.DigitalHuman
{
    [CustomEditor(typeof(SkinTensionRenderer))]
    public class SkinTensionRendererEditor : Editor
    {

        public override void OnInspectorGUI()
        {
            if (target == null)
                return;
#if UNITY_2021_2_OR_NEWER
            var skinTension = target as SkinTensionRenderer;
            if (skinTension == null)
                return;
            SkinDeformationRenderer sdr;
            if (skinTension.TryGetComponent(out sdr))
            {
                if (sdr.executeOnGPU && !skinTension.executeOnGPU)
                {
                    EditorGUILayout.HelpBox(
                        "There is a SkinDeformationRenderer in the same GameObject, which is executed on GPU. The tension calculations are currently done onCPU. This will mean that the skin deformation does not affect the tension calculation. Move the tension evaluation to GPU or SkinDeformation to CPU to fix this.", MessageType.Warning);
                }
            }
#endif
            base.OnInspectorGUI();
        }
    }
}