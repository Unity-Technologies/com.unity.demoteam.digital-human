//#define _SNAPPERS_TEXTURE_ARRAYS

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
						pos = Handles.FreeMoveHandle(pos, Quaternion.identity, hndSize, hndSnap * Vector3.one, Handles.SphereHandleCap);
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

			if (GUILayout.Button("Create Texture Array Assets"))
			{
				CreateTextureArrayAssets(shr);
			}

			base.OnInspectorGUI();
		}

		static void CreateTextureArrayAssets(SnappersHeadRenderer shr)
		{
			var inputMask = new Texture2D[] { shr.mask1, shr.mask2, shr.mask3, shr.mask4, shr.mask5, shr.mask6, shr.mask7, shr.mask8, shr.mask9, shr.mask10, shr.mask11, shr.mask12 };
			var inputAlbedo = new Texture2D[] { shr.albedo1, shr.albedo2, shr.albedo3, shr.albedo4 };
			var inputNormal = new Texture2D[] { shr.normal1, shr.normal2, shr.normal3, shr.normal4 };
			var inputCavity = new Texture2D[] { shr.cavity1, shr.cavity2, shr.cavity3, shr.cavity4 };

			var assetPath = shr.arrayAssetPath.Trim('/');
			var assetPathMask = assetPath + "/_Tex2DArray_Mask.asset";
			var assetPathAlbedo = assetPath + "/_Tex2DArray_Albedo.asset";
			var assetPathNormal = assetPath + "/_Tex2DArray_Normal.asset";
			var assetPathCavity = assetPath + "/_Tex2DArray_Cavity.asset";

			shr.arrayMask = CreateTextureArrayAsset(inputMask, linear: true, assetPathMask);
			shr.arrayAlbedo = CreateTextureArrayAsset(inputAlbedo, linear: false, assetPathAlbedo);
			shr.arrayNormal = CreateTextureArrayAsset(inputNormal, linear: true, assetPathNormal);
			shr.arrayCavity = CreateTextureArrayAsset(inputCavity, linear: true, assetPathCavity);

			EditorUtility.SetDirty(shr);
			AssetDatabase.SaveAssets();
		}

		static Texture2DArray CreateTextureArrayAsset(Texture2D[] slices, bool linear, string path)
		{
			var array = new Texture2DArray(slices[0].width, slices[0].height, slices.Length, slices[0].format, true, linear);
			var arrayWrapper = CreateInstance<BinaryAsset>();

			if (slices.Length > 0 && slices[0] != null)
			{
				array.wrapMode = slices[0].wrapMode;
				array.anisoLevel = slices[0].anisoLevel;
				array.filterMode = slices[0].filterMode;
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
