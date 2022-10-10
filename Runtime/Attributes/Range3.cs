using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.DemoTeam.DigitalHuman
{
	public class Range3Attribute : PropertyAttribute
	{
		public float min;
		public float max;

		public Range3Attribute(float min, float max)
		{
			this.min = min;
			this.max = max;
		}
	}

#if UNITY_EDITOR
	[CustomPropertyDrawer(typeof(Range3Attribute))]
	public class Range3AttributeDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
		{
			var a = (attribute as Range3Attribute);
			var v = property.vector3Value;

			EditorGUI.BeginProperty(rect, label, property);
			EditorGUI.BeginChangeCheck();
			{
				rect = EditorGUI.PrefixLabel(rect, label);

				var controlSpace = 10.0f;
				var controlWidth = (rect.width - 2.0f * controlSpace) / 3.0f;

				rect.width = controlWidth;

				v.x = GUI.HorizontalSlider(rect, v.x, a.min, a.max);
				rect.x += controlWidth + controlSpace;
				v.y = GUI.HorizontalSlider(rect, v.y, a.min, a.max);
				rect.x += controlWidth + controlSpace;
				v.z = GUI.HorizontalSlider(rect, v.z, a.min, a.max);
			}
			if (EditorGUI.EndChangeCheck())
			{
				property.vector3Value = v;
			}
			EditorGUI.EndProperty();
		}
	}
#endif
}
