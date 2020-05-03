//#define _SNAPPERS_TEXTURE_ARRAYS

using UnityEngine;
using UnityEngine.Serialization;
using Unity.DemoTeam.Attributes;

namespace Unity.DemoTeam.DigitalHuman
{
	[ExecuteAlways, RequireComponent(typeof(SkinnedMeshRenderer))]
	public class SnappersHeadRenderer : MonoBehaviour
	{
		private SkinnedMeshRenderer smr;
		private MaterialPropertyBlock smrProps;

		[Header("Facial Rig")]
		public SnappersHeadDefinition.InstanceData headInstance;
		public SnappersHeadDefinition headDefinition;
		[FormerlySerializedAs("headController")]
		public Transform headControllers;
		[EnumFlag]
		public SnappersHeadDefinition.Warnings warnings;

		[Header("Activation masks")]
		public Texture2D mask1;
		public Texture2D mask2;
		public Texture2D mask3;
		public Texture2D mask4;
		public Texture2D mask5;
		public Texture2D mask6;
		public Texture2D mask7;
		public Texture2D mask8;
		public Texture2D mask9;
		public Texture2D mask10;
		public Texture2D mask11;
		public Texture2D mask12;

		[Header("Activation maps")]
		public Texture2D albedo1;
		public Texture2D albedo2;
		public Texture2D albedo3;
		public Texture2D albedo4;
		[Space]
		public Texture2D normal1;
		public Texture2D normal2;
		public Texture2D normal3;
		public Texture2D normal4;
		[Space]
		public Texture2D cavity1;
		public Texture2D cavity2;
		public Texture2D cavity3;
		public Texture2D cavity4;

#if _SNAPPERS_TEXTURE_ARRAYS
		public string arrayAssetPath = "Assets/Characters/Gawain/Face/Snappers";
		public Texture2DArray arrayMask;
		public Texture2DArray arrayAlbedo;
		public Texture2DArray arrayNormal;
		public Texture2DArray arrayCavity;
#endif

		[Header("-> SkinDeformationRenderer (if avail.)")]
		public bool injectFittedWeights;
		[Range(1.0f, 10.0f)]
		public float injectFittedWeightsScale = 1.0f;

		void OnEnable()
		{
			smr = GetComponent<SkinnedMeshRenderer>();
			smrProps = new MaterialPropertyBlock();
		}

		void OnDisable()
		{
			if (smrProps == null)
				smrProps = new MaterialPropertyBlock();

			smr.GetPropertyBlock(smrProps);
			{
				if (headInstance.shaderParamFloats != null)//TODO introduce IsCreated() or similar?
				{
					SnappersHeadDefinition.ResetShaderParam(ref headInstance);
					SnappersHeadDefinition.ApplyShaderParam(ref headInstance, smrProps);
				}
			}
			smr.SetPropertyBlock(smrProps);
		}

		void LateUpdate()
		{
			if (smrProps == null)
				smrProps = new MaterialPropertyBlock();

			smr.GetPropertyBlock(smrProps);
			{
				if (headDefinition != null)
				{
					headDefinition.PrepareInstance(ref headInstance, smr, headControllers, warnings);
					headDefinition.ResolveControllers(ref headInstance);
					headDefinition.ResolveBlendShapes(ref headInstance, smr);

					if (injectFittedWeights)
					{
						var skinDeform = GetComponent<SkinDeformationRenderer>();
						if (skinDeform != null && skinDeform.fittedWeightsAvailable)
						{
							var fittedWeights = skinDeform.fittedWeights;
							var inputIndices = headInstance.blendShapeIndices;
							var inputWeights = headInstance.blendShapeWeights;
							for (int i = 0; i != headInstance.blendShapeIndices.Length; i++)
							{
								inputWeights[i] = Mathf.Clamp01(Mathf.Max(injectFittedWeightsScale * fittedWeights[inputIndices[i]], inputWeights[i]));
							}
						}

						headDefinition.ResolveShaderParam(ref headInstance);
						headDefinition.ResolveBlendShapes(ref headInstance, smr);// done again to avoid factoring in fitted weights
					}
					else
					{
						headDefinition.ResolveShaderParam(ref headInstance);
					}

					SnappersHeadDefinition.ApplyControllers(ref headInstance);
					SnappersHeadDefinition.ApplyBlendShapes(ref headInstance, smr);
					SnappersHeadDefinition.ApplyShaderParam(ref headInstance, smrProps);
				}
				else
				{
					SnappersHeadDefinition.ResetShaderParam(ref headInstance);
					SnappersHeadDefinition.ApplyShaderParam(ref headInstance, smrProps);
				}

#if _SNAPPERS_TEXTURE_ARRAYS
				SetTextureChecked(smrProps, "_SnappersMask", arrayMask);
				SetTextureChecked(smrProps, "_SnappersAlbedo", arrayAlbedo);
				SetTextureChecked(smrProps, "_SnappersNormal", arrayNormal);
				SetTextureChecked(smrProps, "_SnappersCavity", arrayCavity);
#else
				SetTextureChecked(smrProps, "_SnappersMask1", mask1);
				SetTextureChecked(smrProps, "_SnappersMask2", mask2);
				SetTextureChecked(smrProps, "_SnappersMask3", mask3);
				SetTextureChecked(smrProps, "_SnappersMask4", mask4);
				SetTextureChecked(smrProps, "_SnappersMask5", mask5);
				SetTextureChecked(smrProps, "_SnappersMask6", mask6);
				SetTextureChecked(smrProps, "_SnappersMask7", mask7);
				SetTextureChecked(smrProps, "_SnappersMask8", mask8);
				SetTextureChecked(smrProps, "_SnappersMask9", mask9);
				SetTextureChecked(smrProps, "_SnappersMask10", mask10);
				SetTextureChecked(smrProps, "_SnappersMask11", mask11);
				SetTextureChecked(smrProps, "_SnappersMask12", mask12);

				SetTextureChecked(smrProps, "_SnappersAlbedo1", albedo1);
				SetTextureChecked(smrProps, "_SnappersAlbedo2", albedo2);
				SetTextureChecked(smrProps, "_SnappersAlbedo3", albedo3);
				SetTextureChecked(smrProps, "_SnappersAlbedo4", albedo4);

				SetTextureChecked(smrProps, "_SnappersNormal1", normal1);
				SetTextureChecked(smrProps, "_SnappersNormal2", normal2);
				SetTextureChecked(smrProps, "_SnappersNormal3", normal3);
				SetTextureChecked(smrProps, "_SnappersNormal4", normal4);

				SetTextureChecked(smrProps, "_SnappersCavity1", cavity1);
				SetTextureChecked(smrProps, "_SnappersCavity2", cavity2);
				SetTextureChecked(smrProps, "_SnappersCavity3", cavity3);
				SetTextureChecked(smrProps, "_SnappersCavity4", cavity4);
#endif
			}
			smr.SetPropertyBlock(smrProps);
		}

		static void SetTextureChecked(MaterialPropertyBlock props, string textureId, Texture texture)
		{
			if (texture != null)
				props.SetTexture(textureId, texture);
			else
				props.SetTexture(textureId, Texture2D.blackTexture);
		}

#if _SNAPPERS_TEXTURE_ARRAYS
		static void UpdateTextureArray(ref Texture2DArray array, Texture2D[] slices, bool linear)
		{
			if (array != null && array.depth != slices.Length)
			{
				Texture2DArray.Destroy(array);
				array = null;
			}

			if (array == null)
			{
				array = new Texture2DArray(slices[0].width, slices[0].height, slices.Length, slices[0].format, true, linear);
				array.hideFlags |= HideFlags.HideAndDontSave;
			}

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
		}
#endif
	}
}
