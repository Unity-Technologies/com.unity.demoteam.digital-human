using UnityEngine;
using Unity.DemoTeam.Attributes;

namespace Unity.DemoTeam.DigitalHuman
{
	[ExecuteAlways]
	[RequireComponent(typeof(Renderer))]
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
				rndProps.Clear();// TODO would be nice if SetVectorArray didn't truncate ...
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

		//----
		/*
		const int NUM_VERTS = 6;
		const float NUM_VERTS_FLT = 6.0f;
		const int LAST_VERT = NUM_VERTS - 1;
		const float PI = 3.14159265359f;

		static Vector3 normalize(Vector3 v) { return Vector3.Normalize(v); }
		static Vector3 cross(Vector3 a, Vector3 b) { return -Vector3.Cross(a, b); }
		static float dot(Vector3 a, Vector3 b) { return Vector3.Dot(a, b); }
		static float sign(float s) { return Mathf.Sign(s); }
		static float acos(float s) { return Mathf.Acos(s); }

		void SphericalPoly_CalcInteriorAngles(Vector3[] P, float[] A)
		{
			Vector3[] N = new Vector3[NUM_VERTS];

			// calc plane normals
			// where N[i] = normal of incident plane
			//   eg. N[i+0] = cross(C, A);
			//       N[i+1] = cross(A, B);
			{
				N[0] = normalize(cross(P[LAST_VERT], P[0]));
				for (int i = 1; i != NUM_VERTS; i++)
				{
					N[i] = normalize(cross(P[i - 1], P[i]));
				}
			}

			// calc interior angles
			{
				string As = "   A = [ ";
				string Ds = " dot = [ ";
				for (int i = 0; i != LAST_VERT; i++)
				{
					A[i] = PI - sign(dot(N[i], P[i + 1])) * acos(dot(N[i], N[i + 1]));
					{
						As += A[i] + ", ";
						Ds += dot(N[i], P[i + 1]) + ", ";
					}
				}
				A[LAST_VERT] = PI - sign(dot(N[LAST_VERT], P[0])) * acos(dot(N[LAST_VERT], N[0]));
				{
					As += A[LAST_VERT] + " ]";
					Ds += sign(dot(N[LAST_VERT], P[0])) + " ]";
				}
				Debug.Log(As + "\n" + Ds);
			}
		}

		void SphericalPoly_CalcAreaFromInteriorAngles(float[] A, out float area)
		{
			float E = 0.0f;
			for (int i = 0; i != NUM_VERTS; i++)
			{
				E += A[i];
			}
			area = E - (NUM_VERTS_FLT - 2.0f) * PI;
		}

		void SphericalPoly_CalcAreaFromProjectedPositions(Vector3[] P, out float area)
		{
			float[] A = new float[NUM_VERTS];
			SphericalPoly_CalcInteriorAngles(P, A);
			SphericalPoly_CalcAreaFromInteriorAngles(A, out area);
		}

		void DebugSpherical()
		{
			if (skyPolygonDebugSphere == null)
				return;

			Vector3[] P = new Vector3[NUM_VERTS];
			for (int i = 0; i != NUM_VERTS; i++)
			{
				Vector3 vertPos = vertices[i];
				//P[i] = normalize(mul(input.worldToTangent, _TeethSkyPolygon[i].xyz - positionWS));
				P[i] = normalize(vertPos - skyPolygonDebugSphere.position);
			}

			float skyIncident;
			float maxIncident = 2.0f * Mathf.PI;// hemisphere
			SphericalPoly_CalcAreaFromProjectedPositions(P, out skyIncident);

			Debug.Log("skyIncident = " + skyIncident + ", maxIncident = " + maxIncident);
		}*/
	}
}
