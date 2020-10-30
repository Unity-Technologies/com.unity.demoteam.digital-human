//#define _SNAPPERS_TEXTURE_ARRAYS

using System;
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

		[Header("Facial rig")]
		public SnappersHeadDefinition headDefinition;
		public SnappersHeadDefinition.InstanceData headInstance;
		[FormerlySerializedAs("headController")] public Transform headControllers;
		[EnumFlag] public SnappersHeadDefinition.Warnings warnings;

		[Header("Material setup")]
		public TextureSet[] materials = new TextureSet[0];

		[Serializable]
		public struct TextureSet
		{
			[Header("Identifier")]
			public int materialIndex;
			public MaterialPropertyBlock materialProps;

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
#if _SNAPPERS_TEXTURE_ARRAYS
			public Texture2DArray maskArray;
#endif

			[Header("Activation maps")]
			public Texture2D albedo1;
			public Texture2D albedo2;
			public Texture2D albedo3;
			public Texture2D albedo4;
#if _SNAPPERS_TEXTURE_ARRAYS
			public Texture2DArray albedoArray;
#endif

			[Space]
			public Texture2D normal1;
			public Texture2D normal2;
			public Texture2D normal3;
			public Texture2D normal4;
#if _SNAPPERS_TEXTURE_ARRAYS
			public Texture2DArray normalArray;
#endif

			[Space]
			public Texture2D cavity1;
			public Texture2D cavity2;
			public Texture2D cavity3;
			public Texture2D cavity4;
#if _SNAPPERS_TEXTURE_ARRAYS
			public Texture2DArray cavityArray;
#endif

			public void ApplyTextureSet()
			{
#if _SNAPPERS_TEXTURE_ARRAYS
				SetTextureChecked(materialProps, "_SnappersMask", maskArray);
				SetTextureChecked(materialProps, "_SnappersAlbedo", albedoArray);
				SetTextureChecked(materialProps, "_SnappersNormal", normalArray);
				SetTextureChecked(materialProps, "_SnappersCavity", cavityArray);
#else
				SetTextureChecked(materialProps, "_SnappersMask1", mask1);
				SetTextureChecked(materialProps, "_SnappersMask2", mask2);
				SetTextureChecked(materialProps, "_SnappersMask3", mask3);
				SetTextureChecked(materialProps, "_SnappersMask4", mask4);
				SetTextureChecked(materialProps, "_SnappersMask5", mask5);
				SetTextureChecked(materialProps, "_SnappersMask6", mask6);
				SetTextureChecked(materialProps, "_SnappersMask7", mask7);
				SetTextureChecked(materialProps, "_SnappersMask8", mask8);
				SetTextureChecked(materialProps, "_SnappersMask9", mask9);
				SetTextureChecked(materialProps, "_SnappersMask10", mask10);
				SetTextureChecked(materialProps, "_SnappersMask11", mask11);
				SetTextureChecked(materialProps, "_SnappersMask12", mask12);

				SetTextureChecked(materialProps, "_SnappersAlbedo1", albedo1);
				SetTextureChecked(materialProps, "_SnappersAlbedo2", albedo2);
				SetTextureChecked(materialProps, "_SnappersAlbedo3", albedo3);
				SetTextureChecked(materialProps, "_SnappersAlbedo4", albedo4);

				SetTextureChecked(materialProps, "_SnappersNormal1", normal1);
				SetTextureChecked(materialProps, "_SnappersNormal2", normal2);
				SetTextureChecked(materialProps, "_SnappersNormal3", normal3);
				SetTextureChecked(materialProps, "_SnappersNormal4", normal4);

				SetTextureChecked(materialProps, "_SnappersCavity1", cavity1);
				SetTextureChecked(materialProps, "_SnappersCavity2", cavity2);
				SetTextureChecked(materialProps, "_SnappersCavity3", cavity3);
				SetTextureChecked(materialProps, "_SnappersCavity4", cavity4);
#endif
			}
		}

		[Header("-> SkinDeformationRenderer (if avail.)")]
		public bool injectFittedWeights;
		[Range(1.0f, 10.0f)]
		public float injectFittedWeightsScale = 1.0f;

		#region Deprecated material setup
		[HideInInspector, SerializeField] private Texture2D mask1;
		[HideInInspector, SerializeField] private Texture2D mask2;
		[HideInInspector, SerializeField] private Texture2D mask3;
		[HideInInspector, SerializeField] private Texture2D mask4;
		[HideInInspector, SerializeField] private Texture2D mask5;
		[HideInInspector, SerializeField] private Texture2D mask6;
		[HideInInspector, SerializeField] private Texture2D mask7;
		[HideInInspector, SerializeField] private Texture2D mask8;
		[HideInInspector, SerializeField] private Texture2D mask9;
		[HideInInspector, SerializeField] private Texture2D mask10;
		[HideInInspector, SerializeField] private Texture2D mask11;
		[HideInInspector, SerializeField] private Texture2D mask12;
		[HideInInspector, SerializeField] private Texture2D albedo1;
		[HideInInspector, SerializeField] private Texture2D albedo2;
		[HideInInspector, SerializeField] private Texture2D albedo3;
		[HideInInspector, SerializeField] private Texture2D albedo4;
		[HideInInspector, SerializeField] private Texture2D normal1;
		[HideInInspector, SerializeField] private Texture2D normal2;
		[HideInInspector, SerializeField] private Texture2D normal3;
		[HideInInspector, SerializeField] private Texture2D normal4;
		[HideInInspector, SerializeField] private Texture2D cavity1;
		[HideInInspector, SerializeField] private Texture2D cavity2;
		[HideInInspector, SerializeField] private Texture2D cavity3;
		[HideInInspector, SerializeField] private Texture2D cavity4;

		bool TransferMaterial(ref Texture2D src, ref Texture2D dst)
		{
			dst = src;
			src = null;
			return (dst != null);
		}
		#endregion

		void OnEnable()
		{
			#region Deprecated material transfer
			if (materials.Length == 0)
			{
				var setPopulated = false;
				var set = new TextureSet();

				set.materialIndex = 0;
				setPopulated |= TransferMaterial(ref mask1, ref set.mask1);
				setPopulated |= TransferMaterial(ref mask2, ref set.mask2);
				setPopulated |= TransferMaterial(ref mask3, ref set.mask3);
				setPopulated |= TransferMaterial(ref mask4, ref set.mask4);
				setPopulated |= TransferMaterial(ref mask5, ref set.mask5);
				setPopulated |= TransferMaterial(ref mask6, ref set.mask6);
				setPopulated |= TransferMaterial(ref mask7, ref set.mask7);
				setPopulated |= TransferMaterial(ref mask8, ref set.mask8);
				setPopulated |= TransferMaterial(ref mask9, ref set.mask9);
				setPopulated |= TransferMaterial(ref mask10, ref set.mask10);
				setPopulated |= TransferMaterial(ref mask11, ref set.mask11);
				setPopulated |= TransferMaterial(ref mask12, ref set.mask12);
				setPopulated |= TransferMaterial(ref albedo1, ref set.albedo1);
				setPopulated |= TransferMaterial(ref albedo2, ref set.albedo2);
				setPopulated |= TransferMaterial(ref albedo3, ref set.albedo3);
				setPopulated |= TransferMaterial(ref albedo4, ref set.albedo4);
				setPopulated |= TransferMaterial(ref normal1, ref set.normal1);
				setPopulated |= TransferMaterial(ref normal2, ref set.normal2);
				setPopulated |= TransferMaterial(ref normal3, ref set.normal3);
				setPopulated |= TransferMaterial(ref normal4, ref set.normal4);
				setPopulated |= TransferMaterial(ref cavity1, ref set.cavity1);
				setPopulated |= TransferMaterial(ref cavity2, ref set.cavity2);
				setPopulated |= TransferMaterial(ref cavity3, ref set.cavity3);
				setPopulated |= TransferMaterial(ref cavity4, ref set.cavity4);

				if (setPopulated)
				{
					materials = new TextureSet[1];
					materials[0] = set;
				}
			}
			#endregion

			smr = GetComponent<SkinnedMeshRenderer>();

			for (int i = 0; i != materials.Length; i++)
			{
				if (materials[i].materialProps == null)
					materials[i].materialProps = new MaterialPropertyBlock();
			}
		}

		void OnDisable()
		{
			FetchPropertyBlocks();
			{
				if (headInstance.shaderParamFloats != null)//TODO introduce IsCreated() or similar?
				{
					SnappersHeadDefinition.ResetShaderParam(ref headInstance);

					for (int i = 0; i != materials.Length; i++)
					{
						SnappersHeadDefinition.ApplyShaderParam(ref headInstance, materials[i].materialProps);
					}
				}
			}
			ApplyPropertyBlocks();
		}

		void FetchPropertyBlocks()
		{
			for (int i = 0; i != materials.Length; i++)
			{
				if (materials[i].materialProps == null)
					materials[i].materialProps = new MaterialPropertyBlock();

				smr.GetPropertyBlock(materials[i].materialProps, materials[i].materialIndex);
			}
		}

		void ApplyPropertyBlocks()
		{
			for (int i = 0; i != materials.Length; i++)
			{
				smr.SetPropertyBlock(materials[i].materialProps, materials[i].materialIndex);
			}
		}

		void LateUpdate()
		{
			FetchPropertyBlocks();
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
				}
				else
				{
					SnappersHeadDefinition.ResetShaderParam(ref headInstance);
				}

				for (int i = 0; i != materials.Length; i++)
				{
					SnappersHeadDefinition.ApplyShaderParam(ref headInstance, materials[i].materialProps);
					materials[i].ApplyTextureSet();
				}
			}
			ApplyPropertyBlocks();
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
