using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Unity.DemoTeam.DigitalHuman
{
	public class SkinDeformationClipBuildProcessor : IPreprocessBuildWithReport
	{
		public int callbackOrder
		{
			get { return 0; }
		}

		public void OnPreprocessBuild(BuildReport report)
		{
			var clips = Resources.FindObjectsOfTypeAll<SkinDeformationClip>();
			foreach (var clip in clips)
			{
				clip.CopyToStreamingAssets();
				EditorUtility.SetDirty(clip);
			}
			AssetDatabase.SaveAssets();
		}
	}
}
