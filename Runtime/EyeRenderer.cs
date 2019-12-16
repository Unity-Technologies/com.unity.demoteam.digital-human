using UnityEngine;
using Unity.DemoTeam.Attributes;

namespace Unity.DemoTeam.DigitalHuman
{
	[ExecuteAlways]
	[RequireComponent(typeof(Renderer))]
	public class EyeRenderer : MonoBehaviour
	{
		private Renderer rnd;
		private MaterialPropertyBlock rndProps;

		public float eyeRadius = 0.014265f;

		[Space]
		[Tooltip("The Z offset from the eye origin of the iris plane")]
		public float eyeCorneaRadiusStart = 0.01325f;
		[Tooltip("The Z offset from the cornea that contains limbus darkening (the dark ring around the iris)")]
		public float eyeCorneaLimbusDarkeningOffset = 0.00075f;// was 0.001
		[Range(1.0f, 1.4f), Tooltip("The index of refraction of a human cornea is 1.376, but our geometry works well with a value of 1.2. Affects magnitude of iris distortion from cornea lens.")]
		public float eyeCorneaIndexOfRefraction = 1.3f;// was 1.2
		[Range(0.0f, 1.0f)]
		public float eyeCorneaSSSScale = 0.0f;
		[Range(0.0f, 1.0f)]
		public float eyeCorneaSmoothness = 0.917f;
		public bool eyeCorneaDebug = false;

		[Space]
		public bool eyeIrisBentLightingDebug = false;
		public bool eyeIrisBentLighting = true;
		public float eyeIrisPlaneOffset = 0.002f;// was 0
		[Range(-1.0f, 1.0f)]
		public float eyeIrisConcavity = 0.0f;

		[Space]
		[Tooltip("Texture space pupil offset")]
		public Vector2 eyePupilOffset = new Vector2(0.002f, 0.016f);
		[Tooltip("Texture space pupil diameter")]
		public float eyePupilDiameter = 0.095f;
		[Tooltip("Texture space pupil falloff")]
		public float eyePupilFalloff = 0.015f;
		[Tooltip("Texture space pupil scale")]
		public float eyePupilScale = 1.0f;

		[Space]
		public bool eyeWrappedLighting = false;
		[Range(0.0f, 1.0f)]
		public float eyeWrappedLightingCosine = 0.5f;
		[Range(1.0f, 4.0f)]
		public float eyeWrappedLightingPower = 4.0f;

		// http://hyperphysics.phy-astr.gsu.edu/hbase/vision/eyescal.html
		const float IOR_HUMAN_AQUEOUS_HUMOR = 1.336f;
		const float IOR_HUMAN_CORNEA = 1.376f;
		const float IOR_HUMAN_LENS = 1.406f;

		// https://journals.lww.com/optvissci/Abstract/1995/10000/Refractive_Index_and_Osmolality_of_Human_Tears_.4.aspx
		const float IOR_HUMAN_TEARS = 1.33698f;

		[Space]
		[Range(1.0f, 2.0f)]
		public float eyeLitIORCornea = IOR_HUMAN_CORNEA;
		[Range(1.0f, 2.0f)]
		public float eyeLitIORSclera = IOR_HUMAN_TEARS;

		[Space]
		public Transform eyePolygonContainer;
		private const int eyePolygonSize = 4;// should match size in EyeLitForward.shader
		private Vector4[] eyePolygonOS = new Vector4[eyePolygonSize];
		private Vector3 eyeAsgOriginOS = new Vector3(0.0f, 0.0f, 0.0f);
		private Vector3 eyeAsgMeanOS = new Vector3(0.0f, 0.0f, 1.0f);
		private Vector3 eyeAsgTangentOS = new Vector3(1.0f, 0.0f, 0.0f);
		private Vector3 eyeAsgBitangentOS = new Vector3(0.0f, 1.0f, 0.0f);
		private Vector2 eyeAsgSharpness = new Vector2(1.25f, 9.0f);
		[Range(1e-1f, 128.0f)]
		public float eyeAsgPower = 16.0f;
		[Range(1e-7f, 1e-1f)]
		public float eyeAsgThreshold = 1e-1f;
		private Vector2 eyeAsgThresholdScaleBias = new Vector2(1.0f, 0.0f);
		[Range(0.0f, 1.0f)]
		public float eyeAsgModulateAlbedo = 0.5f;

		[Space]
		public ConeMapping coneMapping = ConeMapping.ClosingAxisSpaceSplit;
		public enum ConeMapping
		{
			ObjectSpaceMean,
			ClosingAxisSpaceSplit,
		}
		[VisibleIf("coneMapping", ConeMapping.ClosingAxisSpaceSplit)]
		public float coneOriginOffset = 1.0f;
		[VisibleIf("coneMapping", ConeMapping.ClosingAxisSpaceSplit)]
		public Vector2 coneScale = Vector2.one;
		[VisibleIf("coneMapping", ConeMapping.ClosingAxisSpaceSplit)]
		public Vector3 coneBias = Vector3.zero;
		public bool coneDebug = false;

		void Awake()
		{
			rnd = GetComponent<Renderer>();
			rndProps = new MaterialPropertyBlock();
		}

		// amplitude * e^(pow(sharpness * (cosTheta - 1), power)) = epsilon
		// e^(pow(sharpness * (cosTheta - 1), power)) = epsilon / amplitude
		// pow(sharpness * (cosTheta - 1), power) = log(epsilon / amplitude)
		// pow(sharpness * (cosTheta - 1), power) = log(epsilon) - log(amplitude)
		// sharpness * (cosTheta - 1) = pow(log(epsilon) - log(amplitude), 1.0 / power)
		// sharpness = pow(log(epsilon) - log(amplitude), 1.0 / power) / (cosTheta - 1)

		// https://mynameismjp.wordpress.com/2016/10/09/sg-series-part-2-spherical-gaussians-101/
		float AsgSharpnessFromThreshold(float epsilon, float amplitude, float power, float cosTheta)
		{
			return 0.5f * Mathf.Pow(-Mathf.Log(epsilon) - Mathf.Log(amplitude), 1.0f / power) / -(cosTheta - 1.0f);
		}

		void LateUpdate()
		{
			var eyePolygonComplete = (eyePolygonContainer != null) && (eyePolygonContainer.childCount == eyePolygonSize);
			if (eyePolygonComplete)
			{
				for (int i = 0; i != eyePolygonSize; i++)
				{
					eyePolygonOS[i] = this.transform.InverseTransformPoint(eyePolygonContainer.GetChild(i).position);
					if (coneDebug)
					{
						DrawRayLocal(Vector3.zero, eyePolygonOS[i], Color.white);
					}
				}

				float cosThetaTangent = 0.0f;
				float cosThetaBitangent = 0.0f;

				switch (coneMapping)
				{
					case ConeMapping.ObjectSpaceMean:
						{
							eyeAsgOriginOS = Vector3.zero;
							eyeAsgMeanOS = Vector3.Normalize(
								Vector3.Normalize(eyePolygonOS[0])
								+ Vector3.Normalize(eyePolygonOS[1])
								+ Vector3.Normalize(eyePolygonOS[2])
								+ Vector3.Normalize(eyePolygonOS[3])
							);
							eyeAsgBitangentOS = Vector3.Normalize(
								Vector3.Cross(eyeAsgMeanOS, Vector3.Normalize(eyePolygonOS[2] - eyePolygonOS[0]))
							);
							eyeAsgTangentOS = Vector3.Cross(eyeAsgBitangentOS, eyeAsgMeanOS);

							float cosThetaMeanToLeft = Vector3.Dot(Vector3.Normalize(eyePolygonOS[0]), eyeAsgMeanOS);
							float cosThetaMeanToRight = Vector3.Dot(Vector3.Normalize(eyePolygonOS[2]), eyeAsgMeanOS);
							float cosThetaMeanToTop = Vector3.Dot(Vector3.Normalize(eyePolygonOS[1]), eyeAsgMeanOS);
							float cosThetaMeanToBottom = Vector3.Dot(Vector3.Normalize(eyePolygonOS[3]), eyeAsgMeanOS);

							cosThetaTangent = (cosThetaMeanToLeft + cosThetaMeanToRight) * 0.5f;
							cosThetaBitangent = (cosThetaMeanToTop + cosThetaMeanToBottom) * 0.5f;
						}
						break;
					case ConeMapping.ClosingAxisSpaceSplit:
						{
							var eyePolygonRot = Quaternion.Euler(coneBias.y, coneBias.x, coneBias.z);

							for (int i = 0; i != eyePolygonSize; i++)
							{
								eyePolygonOS[i] = eyePolygonRot * eyePolygonOS[i];
							}

							var closingPlaneNormal = Vector3.Normalize(eyePolygonOS[2] - eyePolygonOS[0]);
							var closingPlaneOrigin = Vector3.ProjectOnPlane(eyePolygonOS[0], closingPlaneNormal);

							var closingPlanePosTop = Vector3.ProjectOnPlane(eyePolygonOS[1], closingPlaneNormal) - closingPlaneOrigin;
							var closingPlanePosBottom = Vector3.ProjectOnPlane(eyePolygonOS[3], closingPlaneNormal) - closingPlaneOrigin;
							var closingPlaneDirTop = Vector3.Normalize(closingPlanePosTop);
							var closingPlaneDirBottom = Vector3.Normalize(closingPlanePosBottom);

							var closingPlaneForward = Vector3.Normalize(closingPlaneDirTop + closingPlaneDirBottom);
							{
								closingPlaneOrigin -= closingPlaneForward * (0.01f * coneOriginOffset);
								//TODO pick an origin that sends the resulting forward vector through the original origin in the closing plane

								closingPlanePosTop = Vector3.ProjectOnPlane(eyePolygonOS[1], closingPlaneNormal) - closingPlaneOrigin;
								closingPlanePosBottom = Vector3.ProjectOnPlane(eyePolygonOS[3], closingPlaneNormal) - closingPlaneOrigin;
								closingPlaneDirTop = Vector3.Normalize(closingPlanePosTop);
								closingPlaneDirBottom = Vector3.Normalize(closingPlanePosBottom);

								closingPlaneForward = Vector3.Normalize(closingPlaneDirTop + closingPlaneDirBottom);
							}

							var openingPosLeft = (Vector3)eyePolygonOS[0] - closingPlaneOrigin;
							var openingPosRight = (Vector3)eyePolygonOS[2] - closingPlaneOrigin;
							var openingDirLeft = Vector3.Normalize(openingPosLeft);
							var openingDirRight = Vector3.Normalize(openingPosRight);

							var closingPlaneAltitude = coneScale.y * 0.5f * Mathf.Deg2Rad * Vector3.Angle(closingPlaneDirTop, closingPlaneDirBottom);
							var closingPlaneAzimuth = coneScale.x * 0.5f * Mathf.Deg2Rad * Vector3.Angle(openingDirLeft, openingDirRight);

							if (coneDebug)
							{
								DrawRayLocal(closingPlaneOrigin, closingPlanePosTop, Color.yellow);
								DrawRayLocal(closingPlaneOrigin, closingPlanePosBottom, Color.yellow);
								DrawRayLocal(closingPlaneOrigin, openingPosLeft, Color.yellow);
								DrawRayLocal(closingPlaneOrigin, openingPosRight, Color.yellow);
							}

							eyeAsgOriginOS = closingPlaneOrigin;
							eyeAsgMeanOS = closingPlaneForward;
							eyeAsgTangentOS = closingPlaneNormal;
							eyeAsgBitangentOS = Vector3.Normalize(Vector3.Cross(eyeAsgMeanOS, eyeAsgTangentOS));

							cosThetaTangent = Mathf.Cos(closingPlaneAzimuth);
							cosThetaBitangent = Mathf.Cos(closingPlaneAltitude);
						}
						break;
				}

				if (coneDebug)
				{
					DrawRayLocal(eyeAsgOriginOS, eyeAsgMeanOS, Color.red);
					DrawRayLocal(eyeAsgOriginOS, eyeAsgBitangentOS, Color.green);
					DrawRayLocal(eyeAsgOriginOS, eyeAsgTangentOS, Color.blue);
				}

				eyeAsgSharpness.x = AsgSharpnessFromThreshold(eyeAsgThreshold, 1.0f, eyeAsgPower, cosThetaTangent);
				eyeAsgSharpness.y = AsgSharpnessFromThreshold(eyeAsgThreshold, 1.0f, eyeAsgPower, cosThetaBitangent);

				eyeAsgThresholdScaleBias.x = 1.0f / (1.0f - eyeAsgThreshold);
				eyeAsgThresholdScaleBias.y = -eyeAsgThreshold / (1.0f - eyeAsgThreshold);
			}
			else
			{
				for (int i = 0; i != eyePolygonSize; i++)
				{
					eyePolygonOS[i] = Vector4.zero;
				}
			}

			if (eyeCorneaDebug)
			{
				var pos = this.transform.position;
				var rot = this.transform.rotation;

				var dirBF = rot * Vector3.forward;
				var dirBT = rot * Vector3.up;
				var dirLR = rot * Vector3.right;

				var rot45 = rot * Quaternion.AngleAxis(45.0f, Vector3.forward);

				var dirBT45 = rot45 * dirBT;
				var dirLR45 = rot45 * dirLR;

				var posIris = pos + dirBF * eyeCorneaRadiusStart;
				var posFade = pos + dirBF * (eyeCorneaRadiusStart + eyeCorneaLimbusDarkeningOffset);
				var posSide = pos + dirLR * eyeRadius;

				var lineIris = eyeRadius * 0.6f;
				{
					Debug.DrawLine(posIris - dirBT * lineIris, posIris + dirBT * lineIris, Color.red);
					Debug.DrawLine(posIris - dirLR * lineIris, posIris + dirLR * lineIris, Color.red);

					Debug.DrawLine(posIris - dirBT45 * lineIris, posIris + dirBT45 * lineIris, Color.red);
					Debug.DrawLine(posIris - dirLR45 * lineIris, posIris + dirLR45 * lineIris, Color.red);
				}

				var lineFade = eyeRadius * 0.5f;
				{
					Debug.DrawLine(posFade - dirBT45 * lineFade, posFade + dirBT45 * lineFade, Color.magenta);
					Debug.DrawLine(posFade - dirLR45 * lineFade, posFade + dirLR45 * lineFade, Color.magenta);

					Debug.DrawLine(posFade - dirBT45 * lineFade, posIris - dirBT45 * lineFade, Color.magenta);
					Debug.DrawLine(posFade + dirBT45 * lineFade, posIris + dirBT45 * lineFade, Color.magenta);

					Debug.DrawLine(posFade - dirLR45 * lineFade, posIris - dirLR45 * lineFade, Color.magenta);
					Debug.DrawLine(posFade + dirLR45 * lineFade, posIris + dirLR45 * lineFade, Color.magenta);
				}

				var lineSide = eyeRadius * 0.25f;
				{
					Debug.DrawLine(posSide - dirBT * lineSide, posSide + dirBT * lineSide, Color.blue);
					Debug.DrawLine(posSide - dirBF * lineSide, posSide + dirBF * lineSide, Color.blue);
				}
			}

			if (rndProps == null)
				rndProps = new MaterialPropertyBlock();

			rnd.GetPropertyBlock(rndProps);
			{
				rndProps.SetFloat("_EyeRadius", eyeRadius);
				rndProps.SetFloat("_EyeCorneaRadiusStart", eyeCorneaRadiusStart);
				rndProps.SetFloat("_EyeCorneaLimbusDarkeningOffset", eyeCorneaLimbusDarkeningOffset);
				rndProps.SetFloat("_EyeCorneaIndexOfRefraction", eyeCorneaIndexOfRefraction);
				rndProps.SetFloat("_EyeCorneaIndexOfRefractionRatio", 1.0f / eyeCorneaIndexOfRefraction);
				rndProps.SetFloat("_EyeCorneaSSS", eyeCorneaSSSScale);
				rndProps.SetFloat("_EyeCorneaSmoothness", eyeCorneaSmoothness);

				rndProps.SetFloat("_EyeIrisBentLightingDebug", eyeIrisBentLightingDebug ? 1 : 0);
				rndProps.SetFloat("_EyeIrisBentLighting", eyeIrisBentLighting ? 1 : 0);
				rndProps.SetFloat("_EyeIrisPlaneOffset", eyeIrisPlaneOffset);
				rndProps.SetFloat("_EyeIrisConcavity", eyeIrisConcavity);

				rndProps.SetVector("_EyePupilOffset", eyePupilOffset);
				rndProps.SetFloat("_EyePupilDiameter", eyePupilDiameter);
				rndProps.SetFloat("_EyePupilFalloff", eyePupilFalloff);
				rndProps.SetFloat("_EyePupilScale", eyePupilScale);

				if (eyeWrappedLighting)
				{
					rndProps.SetFloat("_EyeWrappedLightingCosine", eyeWrappedLightingCosine);
					rndProps.SetFloat("_EyeWrappedLightingPower", eyeWrappedLightingPower);
				}
				else
				{
					rndProps.SetFloat("_EyeWrappedLightingCosine", 0.0f);
					rndProps.SetFloat("_EyeWrappedLightingPower", 1.0f);
				}

				rndProps.SetFloat("_EyeLitIORCornea", eyeLitIORCornea);
				rndProps.SetFloat("_EyeLitIORSclera", eyeLitIORSclera);

				rndProps.SetVector("_EyeAsgOriginOS", eyeAsgOriginOS);
				rndProps.SetVector("_EyeAsgMeanOS", eyeAsgMeanOS);
				rndProps.SetVector("_EyeAsgTangentOS", eyeAsgTangentOS);
				rndProps.SetVector("_EyeAsgBitangentOS", eyeAsgBitangentOS);
				rndProps.SetVector("_EyeAsgSharpness", eyeAsgSharpness);
				rndProps.SetFloat("_EyeAsgPower", eyeAsgPower);
				rndProps.SetVector("_EyeAsgThresholdScaleBias", eyeAsgThresholdScaleBias);
				rndProps.SetFloat("_EyeAsgModulateAlbedo", eyeAsgModulateAlbedo);
			}
			rnd.SetPropertyBlock(rndProps);
		}

		void DrawRayLocal(Vector3 localPosition, Vector3 localVector, Color color)
		{
			Vector3 worldPosition = this.transform.TransformPoint(localPosition);
			Vector3 worldVector = this.transform.TransformVector(localVector);
			Debug.DrawRay(worldPosition, worldVector, color);
		}
	}
}
