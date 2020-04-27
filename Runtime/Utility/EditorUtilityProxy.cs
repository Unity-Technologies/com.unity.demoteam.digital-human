using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	public static class EditorUtilityProxy
	{
		public static bool DisplayCancelableProgressBar(string title, string info, float progress)
		{
#if UNITY_EDITOR
			return UnityEditor.EditorUtility.DisplayCancelableProgressBar(title, info, progress);
#else
			return false;
#endif
		}

		public static void DisplayProgressBar(string title, string info, float progress)
		{
#if UNITY_EDITOR
			UnityEditor.EditorUtility.DisplayProgressBar(title, info, progress);
#endif
		}

		public static void ClearProgressBar()
		{
#if UNITY_EDITOR
			UnityEditor.EditorUtility.ClearProgressBar();
#endif
		}

		public static void SetDirty(Object target)
		{
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(target);
#endif
		}
	}
}
