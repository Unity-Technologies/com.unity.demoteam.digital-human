using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.DemoTeam.Attributes
{
	[AttributeUsage(AttributeTargets.Field)]
	public class ReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
	[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
	public class ReadOnlyAttributeDrawer : PropertyDrawer
	{
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUI.GetPropertyHeight(property, label, true);
		}

		public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
		{
			var enabled = GUI.enabled;
			GUI.enabled = false;
			EditorGUI.PropertyField(rect, property, true);
			GUI.enabled = enabled;
		}
	}
#endif
}
