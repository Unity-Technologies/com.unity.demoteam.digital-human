#define _SNAPPERS_TEXTURE_ARRAYS

using UnityEditor;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(SnappersHeadRenderer))]
	public class SnappersHeadRendererEditor : Editor
	{
		void OnSceneGUI()
		{
			var shr = target as SnappersHeadRenderer;
			if (shr == null)
				return;

			if (shr.headDefinition == null)
				return;

			var rigControllers = shr.headInstance.rigControllers;
			if (rigControllers == null)
				return;

			var rigTransforms = shr.headInstance.rigTransforms;
			if (rigTransforms == null)
				return;

			for (int i = 0; i != rigTransforms.Length; i++)
			{
				var t = rigTransforms[i];
				if (t == null)
					continue;

				var caps = rigControllers[i].caps;

				var numAxisTranslate = 0;
				var numAxisRotate = 0;
				var numAxisScale = 0;

				numAxisTranslate += (caps & SnappersControllerCaps.translateX) != 0 ? 1 : 0;
				numAxisTranslate += (caps & SnappersControllerCaps.translateY) != 0 ? 1 : 0;
				numAxisTranslate += (caps & SnappersControllerCaps.translateZ) != 0 ? 1 : 0;

				numAxisRotate += (caps & SnappersControllerCaps.rotateX) != 0 ? 1 : 0;
				numAxisRotate += (caps & SnappersControllerCaps.rotateY) != 0 ? 1 : 0;
				numAxisRotate += (caps & SnappersControllerCaps.rotateZ) != 0 ? 1 : 0;

				numAxisScale += (caps & SnappersControllerCaps.scaleX) != 0 ? 1 : 0;
				numAxisScale += (caps & SnappersControllerCaps.scaleY) != 0 ? 1 : 0;
				numAxisScale += (caps & SnappersControllerCaps.scaleZ) != 0 ? 1 : 0;

				var dirX = Vector3.right * -1.0f;// flip
				var dirY = Vector3.up;
				var dirZ = Vector3.forward;

				var hndSize = 0.003f;
				var hndSnap = 0.0f;

				if (numAxisTranslate > 0)
				{
					var drawColor = Color.Lerp(Color.blue, Color.white, 0.5f);
					var drawMatrix = t.parent.localToWorldMatrix;

					using (new Handles.DrawingScope(drawColor, drawMatrix))
					{
						var pos = t.localPosition;
						var rot = t.localRotation;

						//switch (numAxisTranslate)
						//{
						//	case 1:
						//		{
						//			if (caps == SnappersControllerCaps.translateX)
						//				pos = Handles.Slider(pos, dirX, hndSize, Handles.SphereHandleCap, hndSnap);
						//			else if (caps == SnappersControllerCaps.translateY)
						//				pos = Handles.Slider(pos, dirY, hndSize, Handles.SphereHandleCap, hndSnap);
						//			else if (caps == SnappersControllerCaps.translateZ)
						//				pos = Handles.Slider(pos, dirZ, hndSize, Handles.SphereHandleCap, hndSnap);
						//		}
						//		break;

						//	case 2:
						//		{
						//			if (caps == (SnappersControllerCaps.translateX | SnappersControllerCaps.translateY))
						//				pos = Handles.Slider2D(pos, dirZ, dirX, dirY, hndSize, Handles.SphereHandleCap, hndSnap);
						//			else if (caps == (SnappersControllerCaps.translateY | SnappersControllerCaps.translateZ))
						//				pos = Handles.Slider2D(pos, -dirX, dirY, dirZ, hndSize, Handles.SphereHandleCap, hndSnap);
						//			else if (caps == (SnappersControllerCaps.translateX | SnappersControllerCaps.translateZ))
						//				pos = Handles.Slider2D(pos, dirY, dirX, dirZ, hndSize, Handles.SphereHandleCap, hndSnap);
						//		}
						//		break;

						//	case 3:
						//		{
						//			pos = Handles.FreeMoveHandle(pos, Quaternion.identity, hndSize, hndSnap * Vector3.one, Handles.SphereHandleCap);
						//		}
						//		break;
						//}

						EditorGUI.BeginChangeCheck();
						#if UNITY_2022_1_OR_NEWER
						var fmh_101_41_637902790171159361 = Quaternion.identity; pos = Handles.FreeMoveHandle(pos, hndSize, hndSnap * Vector3.one, Handles.SphereHandleCap);
						#else
						var fmh_101_41_637902790171159361 = Quaternion.identity; pos = Handles.FreeMoveHandle(pos, Quaternion.identity, hndSize, hndSnap * Vector3.one, Handles.SphereHandleCap);
						#endif
						if (EditorGUI.EndChangeCheck())
						{
							Undo.RecordObject(t, "Move control rig handle");
							t.localPosition = pos;
							t.localRotation = rot;
						}
					}
				}
			}
		}

#if _SNAPPERS_TEXTURE_ARRAYS
		public override void OnInspectorGUI()
		{
			var shr = target as SnappersHeadRenderer;
			if (shr == null)
				return;

			EditorGUILayout.HelpBox("Remember to build texture arrays after assigning or updating textures in the material setup section.", MessageType.Info);

			if (GUILayout.Button("Build texture arrays"))
			{
				BuildTextureArrays(shr);
			}

			EditorGUILayout.Space();

			base.OnInspectorGUI();
		}

		static void BuildTextureArrays(SnappersHeadRenderer shr)
		{
			var textureSets = shr.materials;
			if (textureSets == null)
				return;

			for (int i = 0; i != textureSets.Length; i++)
			{
				ref var ts = ref textureSets[i];

				var inputMask = new Texture2D[] { ts.mask1, ts.mask2, ts.mask3, ts.mask4, ts.mask5, ts.mask6, ts.mask7, ts.mask8, ts.mask9, ts.mask10, ts.mask11, ts.mask12 };
				var inputAlbedo = new Texture2D[] { ts.albedo1, ts.albedo2, ts.albedo3, ts.albedo4 };
				var inputNormal = new Texture2D[] { ts.normal1, ts.normal2, ts.normal3, ts.normal4 };
				var inputCavity = new Texture2D[] { ts.cavity1, ts.cavity2, ts.cavity3, ts.cavity4 };

				ts.maskArray = CreateTextureArrayAsset(inputMask, linear: true);
				ts.albedoArray = CreateTextureArrayAsset(inputAlbedo, linear: false);
				ts.normalArray = CreateTextureArrayAsset(inputNormal, linear: true);
				ts.cavityArray = CreateTextureArrayAsset(inputCavity, linear: true);
			}

			EditorUtility.SetDirty(shr);
			AssetDatabase.SaveAssets();
		}

		static Texture2DArray CreateTextureArrayAsset(Texture2D[] slices, bool linear)
		{
			var first = System.Array.Find(slices, e => e != null);
			if (first == null)
				return null;

			var path = AssetDatabase.GetAssetPath(first);

			var lastDot = path.LastIndexOf('.');
			if (lastDot > path.LastIndexOf('/'))
			{
				path = path.Substring(0, lastDot) + "_array.asset";
			}
			else
			{
				path = path + "_array.asset";
			}

			return CreateTextureArrayAsset(slices, linear, path);
		}

		static Texture2DArray CreateTextureArrayAsset(Texture2D[] slices, bool linear, string path)
		{
			var first = System.Array.Find(slices, e => e != null);
			if (first == null)
				return null;

			var array = new Texture2DArray(first.width, first.height, slices.Length, first.format, true, linear);
			var arrayWrapper = CreateInstance<BinaryAsset>();

			if (slices.Length > 0)
			{
				array.wrapMode = first.wrapMode;
				array.anisoLevel = first.anisoLevel;
				array.filterMode = first.filterMode;
			}

			for (int i = 0; i != slices.Length; i++)
			{
				if (slices[i] != null)
				{
					int mipCount = slices[i].mipmapCount;
					for (int mip = 0; mip != mipCount; mip++)
					{
						Graphics.CopyTexture(slices[i], 0, mip, array, i, mip);
					}
				}
			}

			AssetDatabase.CreateAsset(arrayWrapper, path);
			AssetDatabase.AddObjectToAsset(array, arrayWrapper);

			array.name = arrayWrapper.name;

			return array;
		}
#endif
	}
}
