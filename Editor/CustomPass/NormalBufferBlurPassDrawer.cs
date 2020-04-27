using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomPassDrawer(typeof(NormalBufferBlurPass))]
	class NormalBufferBlurPassDrawer : CustomPassDrawer
	{
		static readonly float lineFeed = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

		SerializedProperty queue;
		SerializedProperty layerMask;

		protected override void Initialize(SerializedProperty customPass)
		{
			queue = customPass.FindPropertyRelative("queue");
			layerMask = customPass.FindPropertyRelative("layerMask");
		}

		protected override PassUIFlag commonPassUIFlags => PassUIFlag.Name;

		protected override float GetPassHeight(SerializedProperty customPass)
		{
			float height = base.GetPassHeight(customPass);

			height += lineFeed;
			height += lineFeed;

			return height;
		}

		protected override void DoPassGUI(SerializedProperty customPass, Rect rect)
		{
			base.DoPassGUI(customPass, rect);

			// queue
			queue.intValue = (int)(CustomPass.RenderQueueType)EditorGUI.EnumPopup(rect, "Queue", (CustomPass.RenderQueueType)queue.intValue);
			rect.y += lineFeed;

			// layerMask
			EditorGUI.PropertyField(rect, layerMask);
			rect.y += lineFeed;
		}
	}
}
