using UnityEditor;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SnappersHeadImporter)), CanEditMultipleObjects]
	public class SnappersHeadImporterEditor : Editor
	{
		const string ASSETS_STRING = @"The following files will be written:

  {0}_SnappersHead.asset
  {0}_SnappersHead.cs
  {0}_SnappersControllers.cs
  {0}_SnappersBlendShapes.cs

(and overwritten if they already exist)";

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			EditorGUILayout.Space();

			var singleTarget = (targets.Length == 1);
			if (singleTarget)
			{
				EditorGUILayout.HelpBox(string.Format(ASSETS_STRING, (target as SnappersHeadImporter).csClassPrefix), MessageType.Info);
			}
			else
			{
				EditorGUILayout.HelpBox(string.Format(ASSETS_STRING, "[Multiple Prefixes]"), MessageType.Warning);
			}

			if (GUILayout.Button(singleTarget ? "Generate" : "Generate all selected"))
			{
				for (int i = 0; i != targets.Length; i++)
				{
					if (EditorUtility.DisplayCancelableProgressBar("Processing...", target.name, i / (targets.Length)) == false)
					{
						var generator = (SnappersHeadImporter)target;
						if (generator != null)
						{
							generator.Generate();
						}
					}
				}
				EditorUtility.ClearProgressBar();
			}
		}
	}
}