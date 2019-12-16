using UnityEditor;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SkinDeformationPlayableAsset))]
	public class SkinDeformationPlayableAssetEditor : Editor
	{
		private Editor clipEditor;

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			if (target != null)
			{
				var clip = (target as SkinDeformationPlayableAsset).clip;
				if (clip != null)
				{
					Editor.CreateCachedEditor(clip, null, ref clipEditor);

					clipEditor.DrawHeader();
					clipEditor.OnInspectorGUI();
				}
			}
		}
	}
}
