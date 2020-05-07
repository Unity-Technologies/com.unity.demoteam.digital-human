using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.Experimental.SceneManagement;
#endif

namespace Unity.DemoTeam.DigitalHuman
{
	public class PrefabTransformHierarchy : MonoBehaviour
	{
		[Serializable]
		public struct TransformDefaults
		{
			public Transform transform;
			public Vector3 localPosition;
			public Quaternion localRotation;
		}

		public TransformDefaults[] data;

		public void LoadDefaults()
		{
			if (data == null)
				return;

			for (int i = 0; i != data.Length; i++)
			{
				if (data[i].transform == null)
					continue;

				data[i].transform.localPosition = data[i].localPosition;
				data[i].transform.localRotation = data[i].localRotation;
			}
		}

		public void SaveDefaults()
		{
			var transforms = this.transform.GetComponentsInChildren<Transform>(includeInactive: true);
			var transformCount = transforms.Length;

			data = new TransformDefaults[transformCount];

			for (int i = 0; i != transformCount; i++)
			{
				data[i].transform = transforms[i];
				data[i].localPosition = transforms[i].localPosition;
				data[i].localRotation = transforms[i].localRotation;
			}
		}

#if UNITY_EDITOR
		static PrefabTransformHierarchy()
		{
			PrefabStage.prefabSaving += (GameObject prefab) =>
			{
				var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
				if (prefabStage != null)
				{
					var roots = prefab.GetComponentsInChildren<PrefabTransformHierarchy>(includeInactive: true);
					foreach (var root in roots)
					{
						root.SaveDefaults();
					}
				}
			};
		}
#endif
	}
}
