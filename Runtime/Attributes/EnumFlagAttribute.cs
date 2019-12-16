using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.DemoTeam.Attributes
{
	[AttributeUsage(AttributeTargets.Field)]
	public class EnumFlagAttribute : PropertyAttribute { }

#if UNITY_EDITOR
	[CustomPropertyDrawer(typeof(EnumFlagAttribute))]
	public class EnumFlagDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			Enum targetEnum = (Enum)fieldInfo.GetValue(property.serializedObject.targetObject);
			Enum enumValue = EditorGUI.EnumFlagsField(position, label, targetEnum);
			property.intValue = (int)Convert.ChangeType(enumValue, targetEnum.GetType());
		}
	}
#endif
}
