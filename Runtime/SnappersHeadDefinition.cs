using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public abstract class SnappersHeadDefinition : ScriptableObject
	{
		public struct InstanceData
		{
			public SnappersHeadDefinition definition;//TODO replace with hash
			public Mesh sourceMesh;
			public Transform sourceRig;
			public Transform[] rigTransforms;
			public SnappersController[] rigControllers;
			public int blendShapeCount;
			public int[] blendShapeIndices;
			public float[] blendShapeWeights;
			public float[] shaderParamFloats;
		}

		public abstract InstanceData CreateInstanceData(Mesh sourceMesh, Transform sourceRig, Warnings warnings);
		public abstract unsafe void ResolveControllers(void* ptrSnappersControllers, void* ptrSnappersBlendShapes, void* ptrSnappersShaderParam);
		public abstract unsafe void ResolveBlendShapes(void* ptrSnappersControllers, void* ptrSnappersBlendShapes, void* ptrSnappersShaderParam);
		public abstract unsafe void ResolveShaderParam(void* ptrSnappersControllers, void* ptrSnappersBlendShapes, void* ptrSnappersShaderParam);
		public abstract unsafe void InitializeControllerCaps(void* ptrSnappersControllers);

		[Flags]
		public enum Warnings : int
		{
			None = 0,
			MissingTransforms = 1 << 0,
			MissingBlendShapes = 1 << 1,
		}

		public InstanceData CreateInstanceData<NamedControllers, NamedBlendShapes>(Mesh sourceMesh, Transform sourceRig, Warnings warnings) where NamedControllers : struct where NamedBlendShapes : struct
		{
			var blendShapePrefix = "";
			var blendShapeNames = typeof(NamedBlendShapes).GetFields();

			if (sourceMesh.blendShapeCount > 0)
			{
				var firstBlendShapeName = sourceMesh.GetBlendShapeName(0);
				var firstBlendShapeDelim = firstBlendShapeName.IndexOf('.');
				if (firstBlendShapeDelim != -1)
				{
					blendShapePrefix = firstBlendShapeName.Substring(0, firstBlendShapeDelim + 1);
				}
			}

			var controllerPrefix = string.Empty;
			var controllerNames = typeof(NamedControllers).GetFields();

			InstanceData instanceData;
			instanceData.definition = this;
			instanceData.sourceMesh = sourceMesh;
			instanceData.sourceRig = sourceRig;
			instanceData.rigTransforms = new Transform[controllerNames.Length];
			instanceData.rigControllers = new SnappersController[controllerNames.Length];
			instanceData.blendShapeCount = sourceMesh.blendShapeCount;
			instanceData.blendShapeIndices = new int[blendShapeNames.Length];
			instanceData.blendShapeWeights = new float[blendShapeNames.Length];
			instanceData.shaderParamFloats = new float[UnsafeUtility.SizeOf<SnappersShaderParam>() / sizeof(float)];

			//Debug.Log("CreateInstanceData for " + sourceMesh + " (blendShapePrefix = " + blendShapePrefix + ")");

			unsafe
			{
				fixed (void* ptrSnappersControllers = instanceData.rigControllers)
				{
					InitializeControllerCaps(ptrSnappersControllers);
				}
			}

			if (sourceRig != null)
			{
				for (int i = 0; i != controllerNames.Length; i++)
				{
					var transform = FindRecursive(sourceRig, controllerPrefix + controllerNames[i].Name);
					if (transform != null)
						instanceData.rigTransforms[i] = transform;
					else if (warnings.HasFlag(Warnings.MissingTransforms))
						Debug.LogWarningFormat("rig definition {0} targets transform not present in linked rig: {1}", this.name, controllerPrefix + controllerNames[i].Name);
				}
			}

			for (int i = 0; i != blendShapeNames.Length; i++)
			{
				int blendShapeIndex = sourceMesh.GetBlendShapeIndex(blendShapePrefix + blendShapeNames[i].Name);
				if (blendShapeIndex != -1)
					instanceData.blendShapeIndices[i] = blendShapeIndex;
				else if (warnings.HasFlag(Warnings.MissingBlendShapes))
					Debug.LogWarningFormat("rig definition {0} targets blend shape not present in linked mesh: {1}", this.name, blendShapePrefix + blendShapeNames[i].Name);
			}

			//Debug.LogFormat("{0} discovered blendShapeIndices: {1}", this.name, string.Join(",", instanceData.blendShapeIndices));

			return instanceData;
		}

		static Transform FindRecursive(Transform transform, string name)
		{
			if (transform.name == name)
			{
				return transform;
			}
			else
			{
				for (int i = 0; i != transform.childCount; i++)
				{
					var child = transform.GetChild(i);
					var childResult = FindRecursive(child, name);
					if (childResult != null)
						return childResult;
				}

				return null;
			}
		}

		public void PrepareInstance(ref InstanceData instanceData, SkinnedMeshRenderer smr, Transform sourceRig, Warnings warnings)
		{
			var inputsPossiblyChanged = false;
			{
				inputsPossiblyChanged |= (instanceData.definition != this);
				inputsPossiblyChanged |= (instanceData.sourceMesh != smr.sharedMesh);
				inputsPossiblyChanged |= (instanceData.sourceRig != sourceRig);
				inputsPossiblyChanged |= (instanceData.blendShapeCount != smr.sharedMesh.blendShapeCount);
			}
			if (inputsPossiblyChanged)
			{
				instanceData = CreateInstanceData(smr.sharedMesh, sourceRig, warnings);
			}
		}

		public void ResolveControllers(ref InstanceData instanceData)
		{
			const float mul_100 = 100.0f;
			for (int i = 0; i != instanceData.rigTransforms.Length; i++)
			{
				var rigTransform = instanceData.rigTransforms[i];
				if (rigTransform != null)
				{
					Vector3 localPos = rigTransform.localPosition;
					Vector3 localRot = rigTransform.localRotation.eulerAngles;
					Vector3 localMul = rigTransform.localScale;

					instanceData.rigControllers[i].translateX = localPos.x * mul_100 * -1.0f;// flip
					instanceData.rigControllers[i].translateY = localPos.y * mul_100;
					instanceData.rigControllers[i].translateZ = localPos.z * mul_100;

					instanceData.rigControllers[i].rotateX = localRot.x * -1.0f;
					instanceData.rigControllers[i].rotateY = localRot.y;
					instanceData.rigControllers[i].rotateZ = localRot.z;

					instanceData.rigControllers[i].scaleX = localMul.x;
					instanceData.rigControllers[i].scaleY = localMul.y;
					instanceData.rigControllers[i].scaleZ = localMul.z;
				}
				//else
				//{
				//	Debug.LogError("bad transform index: " + i);
				//}
			}

			unsafe
			{
				fixed (void* ptrSnappersControllers = instanceData.rigControllers)
				fixed (void* ptrSnappersBlendShapes = instanceData.blendShapeWeights)
				fixed (void* ptrSnappersShaderParam = instanceData.shaderParamFloats)
				{
					ResolveControllers(ptrSnappersControllers, ptrSnappersBlendShapes, ptrSnappersShaderParam);
				}
			}
		}

		public void ResolveBlendShapes(ref InstanceData instanceData, SkinnedMeshRenderer smr)
		{
			const float rcp_100 = 1.0f / 100.0f;
			for (int i = 0; i != instanceData.blendShapeIndices.Length; i++)
			{
				int blendShapeIndex = instanceData.blendShapeIndices[i];
				if (blendShapeIndex != -1)
				{
					instanceData.blendShapeWeights[i] = smr.GetBlendShapeWeight(blendShapeIndex) * rcp_100;
				}
				//else
				//{
				//	Debug.LogError("bad blendshape index: " + blendShapeIndex);
				//}
			}

			unsafe
			{
				fixed (void* ptrSnappersControllers = instanceData.rigControllers)
				fixed (void* ptrSnappersBlendShapes = instanceData.blendShapeWeights)
				fixed (void* ptrSnappersShaderParam = instanceData.shaderParamFloats)
				{
					ResolveBlendShapes(ptrSnappersControllers, ptrSnappersBlendShapes, ptrSnappersShaderParam);
				}
			}
		}

		public void ResolveShaderParam(ref InstanceData instanceData)
		{
			unsafe
			{
				fixed (void* ptrSnappersControllers = instanceData.rigControllers)
				fixed (void* ptrSnappersBlendShapes = instanceData.blendShapeWeights)
				fixed (void* ptrSnappersShaderParam = instanceData.shaderParamFloats)
				{
					ResolveShaderParam(ptrSnappersControllers, ptrSnappersBlendShapes, ptrSnappersShaderParam);
				}
			}
		}

		public static void ResetShaderParam(ref InstanceData instanceData)
		{
			ArrayUtils.ClearChecked(instanceData.shaderParamFloats);
		}

		public static void ApplyControllers(ref InstanceData instanceData)
		{
			const float rcp_100 = 1.0f / 100.0f;
			for (int i = 0; i != instanceData.rigTransforms.Length; i++)
			{
				var rigTransform = instanceData.rigTransforms[i];
				if (rigTransform != null)
				{
					Vector3 localPos;
					Vector3 localRot;
					Vector3 localMul;

					localPos.x = instanceData.rigControllers[i].translateX * rcp_100 * -1.0f;// flip
					localPos.y = instanceData.rigControllers[i].translateY * rcp_100;
					localPos.z = instanceData.rigControllers[i].translateZ * rcp_100;

					localRot.x = instanceData.rigControllers[i].rotateX * -1.0f;
					localRot.y = instanceData.rigControllers[i].rotateY;
					localRot.z = instanceData.rigControllers[i].rotateZ;

					localMul.x = instanceData.rigControllers[i].scaleX;
					localMul.y = instanceData.rigControllers[i].scaleY;
					localMul.z = instanceData.rigControllers[i].scaleZ;

					rigTransform.localPosition = localPos;
					rigTransform.localRotation = Quaternion.Euler(localRot);
					rigTransform.localScale = localMul;
				}
				//else
				//{
				//	Debug.LogError("bad transform index: " + i);
				//}
			}
		}

		public static void ApplyBlendShapes(ref InstanceData instanceData, SkinnedMeshRenderer smr)
		{
			const float mul_100 = 100.0f;
			for (int i = 0; i != instanceData.blendShapeIndices.Length; i++)
			{
				int blendShapeIndex = instanceData.blendShapeIndices[i];
				if (blendShapeIndex != -1)
				{
					smr.SetBlendShapeWeight(blendShapeIndex, instanceData.blendShapeWeights[i] * mul_100);
				}
				//else
				//{
				//	Debug.LogError("bad blendshape index: " + blendShapeIndex);
				//}
			}
		}

		public static void ApplyShaderParam(ref InstanceData instanceData, MaterialPropertyBlock smrProps)
		{
			smrProps.SetFloatArray("_SnappersMaskParams", instanceData.shaderParamFloats);
		}
	}

	public struct SnappersController
	{
		public float translateX;
		public float translateY;
		public float translateZ;

		public float rotateX;
		public float rotateY;
		public float rotateZ;

		public float scaleX;
		public float scaleY;
		public float scaleZ;

		public SnappersControllerCaps caps;
	}

	[Flags]
	public enum SnappersControllerCaps : uint
	{
		none = 0,

		translateX = 1 << 0,
		translateY = 1 << 1,
		translateZ = 1 << 2,

		rotateX = 1 << 3,
		rotateY = 1 << 4,
		rotateZ = 1 << 5,

		scaleX = 1 << 6,
		scaleY = 1 << 7,
		scaleZ = 1 << 8,
	}

	public struct SnappersShaderParam
	{
		public float Mask1;
		public float Mask2;
		public float Mask3;
		public float Mask4;
		public float Mask5;
		public float Mask6;
		public float Mask7;
		public float Mask8;
		public float Mask9;
		public float Mask10;
		public float Mask11;
		public float Mask12;
		public float Mask13;
		public float Mask14;
		public float Mask15;
		public float Mask16;
		public float Mask17;
		public float Mask18;
		public float Mask19;
		public float Mask20;
		public float Mask21;
		public float Mask22;
		public float Mask23;
		public float Mask24;
		public float Mask25;
		public float Mask26;
		public float Mask27;
		public float Mask28;
		public float Mask29;
		public float Mask30;
		public float Mask31;
		public float Mask32;
		public float Mask33;
		public float Mask34;
		public float Mask35;
		public float Mask36;
		public float Mask37;
		public float Mask38;
		public float Mask39;
		public float Mask40;
		public float Mask41;
		public float Mask42;
		public float Mask43;
		public float Mask44;
		public float Mask45;
		public float Mask46;
		public float Mask47;
		public float Mask48;
		public float Mask49;
		public float Mask50;
		public float Mask51;
		public float Mask52;
		public float Mask53;
		public float Mask54;
		public float Mask55;
		public float Mask56;
		public float Mask57;
		public float Mask58;
		public float Mask59;
		public float Mask60;
		public float Mask61;
		public float Mask62;
		public float Mask63;
		public float Mask64;
		public float Mask65;
		public float Mask66;
		public float Mask67;
		public float Mask68;
		public float Mask69;
		public float Mask70;
		public float Mask71;
		public float Mask72;
		public float Mask73;
		public float Mask74;
		public float Mask75;
		public float Mask76;
		public float Mask77;
		public float Mask78;
		public float Mask79;
		public float Mask80;
		public float Mask81;
		public float Mask82;
		public float Mask83;
		public float Mask84;
		public float Mask85;
		public float Mask86;
		public float Mask87;
		public float Mask88;
		public float Mask89;
		public float Mask90;
		public float Mask91;
		public float Mask92;
		public float Mask93;
		public float Mask94;
		public float Mask95;
		public float Mask96;
		public float Mask97;
		public float Mask98;
		public float Mask99;
		public float Mask100;
		public float Mask101;
		public float Mask102;
		public float Mask103;
		public float Mask104;
		public float Mask105;
		public float Mask106;
		public float Mask107;
		public float Mask108;
		public float Mask109;
		public float Mask110;
		public float Mask111;
		public float Mask112;
		public float Mask113;
		public float Mask114;
		public float Mask115;
		public float Mask116;
		public float Mask117;
		public float Mask118;
		public float Mask119;
		public float Mask120;
		public float Mask121;
		public float Mask122;
		public float Mask123;
		public float Mask124;
		public float Mask125;
		public float Mask126;
		public float Mask127;
		public float Mask128;
		public float Mask129;
		public float Mask130;
		public float Mask131;
		public float Mask132;
		public float Mask133;
		public float Mask134;
		public float Mask135;
	}
}
