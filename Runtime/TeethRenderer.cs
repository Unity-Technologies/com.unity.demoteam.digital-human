using UnityEngine;
using Unity.DemoTeam.Attributes;

namespace Unity.DemoTeam.DigitalHuman
{
	[ExecuteAlways, RequireComponent(typeof(Renderer))]
	public class TeethRenderer : MonoBehaviour
	{
		private Renderer rnd;
		private Material rndMat;
		private MaterialPropertyBlock rndProps;

		private const uint vertexFixedBit6 = 1 << 5;
		private const uint vertexFixedMask = vertexFixedBit6;
		private const int vertexLimit = 32;// should match limit in LitTeeth.shader
		private Vector4[] vertexData = new Vector4[0];

		[Range(0.0f, 1.0f)] public float litPotentialMin = 0.0f;
		[Range(0.0f, 1.0f)] public float litPotentialMax = 1.0f;
		[Min(1.0f)] public float litPotentialFalloff = 4.0f;

		public Attenuation mode;
		public enum Attenuation
		{
			None,
			Linear,
			SkyPolygon,
		}

		[EditableIf("mode", Attenuation.Linear)] public Transform linearBack;
		[EditableIf("mode", Attenuation.Linear)] public Transform linearFront;

		[EditableIf("mode", Attenuation.SkyPolygon)] public Transform skyPolygonContainer;
		[EditableIf("mode", Attenuation.SkyPolygon)] public Transform skyPolygonDebugSphere;

		public bool showDebugWireframe;

		void Awake()
		{
			rnd = GetComponent<Renderer>();
#if UNITY_EDITOR
			rndMat = rnd.sharedMaterial;
#else
			rndMat = rnd.material;
#endif
			rndProps = new MaterialPropertyBlock();
		}

		void PrepareKeyword(string keyword, bool enabled)
		{
#if UNITY_EDITOR
			if (rndMat != rnd.sharedMaterial)
				rndMat = rnd.sharedMaterial;
#endif

			if (rndMat.IsKeywordEnabled(keyword) != enabled)
			{
				if (enabled)
					rndMat.EnableKeyword(keyword);
				else
					rndMat.DisableKeyword(keyword);
			}
		}

		void PrepareVertexData(int vertexCount)
		{
			if (vertexData.Length != vertexCount)
				vertexData = new Vector4[vertexCount];
		}

		void LateUpdate()
		{
			var outputAttn = mode;
			var outputSize = 0;

			switch (outputAttn)
			{
				case Attenuation.None:
					break;

				case Attenuation.Linear:
					if (linearFront == null || linearBack == null)
						outputAttn = Attenuation.None;
					else
						outputSize = 6;
					break;

				case Attenuation.SkyPolygon:
					if (skyPolygonContainer == null || skyPolygonContainer.childCount < 3)
						outputAttn = Attenuation.None;
					else
						outputSize = Mathf.Min(vertexLimit, skyPolygonContainer.childCount);
					break;
			}

			var outputFixedBit = outputSize > 0 ? (1u << (outputSize - 1)) : 0u;
			var outputFixedSize = (vertexFixedMask & outputFixedBit) != 0;

			PrepareKeyword("TEETH_ATTN_NONE", outputAttn == Attenuation.None);
			PrepareKeyword("TEETH_ATTN_LINEAR", outputAttn == Attenuation.Linear);
			PrepareKeyword("TEETH_ATTN_SKYPOLYGON", outputAttn == Attenuation.SkyPolygon);

			PrepareKeyword("TEETH_DATA_FIXED_6", outputFixedSize && outputSize == 6);
			PrepareKeyword("TEETH_DATA_VARIABLE_32", !outputFixedSize);

			if (outputFixedSize)
				PrepareVertexData(outputSize);
			else
				PrepareVertexData(vertexLimit);

			switch (outputAttn)
			{
				case Attenuation.Linear:
					vertexData[0] = linearFront.position;
					vertexData[1] = linearBack.position;
					break;

				case Attenuation.SkyPolygon:
					for (int i = 0; i != outputSize; i++)
					{
						vertexData[i] = skyPolygonContainer.GetChild(i).position;
					}
					break;
			}

			if (rndProps == null)
				rndProps = new MaterialPropertyBlock();

			rnd.GetPropertyBlock(rndProps);
			{
				rndProps.Clear();//TODO would be nice if SetVectorArray didn't truncate ...
				rndProps.SetVector("_TeethParams", new Vector4(litPotentialMin, litPotentialMax, litPotentialFalloff));
				rndProps.SetVectorArray("_TeethVertexData", vertexData);
				rndProps.SetInt("_TeethVertexCount", outputSize);
			}
			rnd.SetPropertyBlock(rndProps);

			//DebugSpherical();
		}

		void OnDrawGizmos()
		{
			if (!showDebugWireframe)
				return;

			Gizmos.color = Color.yellow;
			Gizmos.matrix = Matrix4x4.identity;

			switch (mode)
			{
				case Attenuation.Linear:
					if (linearFront != null || linearBack != null)
					{
						int vertexCount = 2;
						if (vertexCount > vertexData.Length)
							break;

						Gizmos.DrawLine(vertexData[0], vertexData[1]);
					}
					break;

				case Attenuation.SkyPolygon:
					if (skyPolygonContainer != null)
					{
						int vertexCount = skyPolygonContainer.childCount;
						if (vertexCount > vertexLimit)
							vertexCount = vertexLimit;
						if (vertexCount > vertexData.Length)
							vertexCount = vertexData.Length;
						if (vertexCount > 0)
						{
							for (int i = 1; i != vertexCount; i++)
							{
								Gizmos.DrawLine(vertexData[i - 1], vertexData[i]);
							}
							Gizmos.DrawLine(vertexData[vertexCount - 1], vertexData[0]);
						}

						if (skyPolygonDebugSphere != null)
						{
							var origin = skyPolygonDebugSphere.position;
							var radius = skyPolygonDebugSphere.localScale.x * 0.5f;

							for (int i = 0; i != vertexCount; i++)
							{
								Vector3 v0 = vertexData[i];
								Vector3 v1 = vertexData[(i + 1) % vertexCount];
								Vector3 A = radius * Vector3.Normalize(v0 - origin);
								Vector3 B = radius * Vector3.Normalize(v1 - origin);

								Gizmos_DrawArc(origin, A, B);
							}
						}

					}
					break;
			}
		}

		void Gizmos_DrawArc(Vector3 O, Vector3 A, Vector3 B, int segments = 100)
		{
			var arcRot = Quaternion.FromToRotation(A, B);
			var preRot = Quaternion.identity;
			var rcpSeg = 1.0f / segments;
			for (int i = 0; i != segments; i++)
			{
				var preDir = preRot * A;
				var curRot = preRot = Quaternion.Slerp(Quaternion.identity, arcRot, (i + 1) * rcpSeg);
				var curDir = curRot * A;
				Gizmos.DrawLine(O + preDir, O + curDir);
			}
		}
	}
}
