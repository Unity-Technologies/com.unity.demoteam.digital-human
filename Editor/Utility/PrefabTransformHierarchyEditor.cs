using UnityEditor;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(PrefabTransformHierarchy))]
	public class PrefabTransformHierarchyEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			var root = target as PrefabTransformHierarchy;
			if (root != null)
			{
				if (GUILayout.Button("Revert transform hierarchy"))
				{
					if (Application.isPlaying)
					{
						root.LoadDefaults();
					}
					else
					{
						var transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
						for (int i = 0; i != transforms.Length; i++)
						{
							EditorUtility.DisplayProgressBar("Reverting transforms ...", (i + 1) + " / " + transforms.Length, (float)i / transforms.Length);

							var transform = transforms[i];
							if (transform != null)
							{
								PrefabUtility.RevertObjectOverride(transforms[i], InteractionMode.UserAction);
							}
						}
					}

					EditorUtility.ClearProgressBar();
					EditorUtility.SetDirty(root);
				}
			}
		}
	}
}
