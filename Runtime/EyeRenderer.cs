using System;
using UnityEngine;
using UnityEngine.Serialization;
using Unity.DemoTeam.Attributes;

namespace Unity.DemoTeam.DigitalHuman
{
	[ExecuteAlways, RequireComponent(typeof(Renderer))]
	public class EyeRenderer : MonoBehaviour
	{
		private Renderer rnd;
		private MaterialPropertyBlock rndProps;

		// http://hyperphysics.phy-astr.gsu.edu/hbase/vision/eyescal.html
		const float IOR_HUMAN_AQUEOUS_HUMOR = 1.336f;
		const float IOR_HUMAN_CORNEA = 1.376f;
		const float IOR_HUMAN_LENS = 1.406f;

		// https://journals.lww.com/optvissci/Abstract/1995/10000/Refractive_Index_and_Osmolality_of_Human_Tears_.4.aspx
		const float IOR_HUMAN_TEARS = 1.33698f;

		[Header("Geometry")]
		public float geometryRadius = 0.014265f;
		public Vector3 geometryOrigin = Vector3.zero;
		public Vector3 geometryAngle = Vector3.zero;

		[Header("Sclera")]
		[FormerlySerializedAs("eyeLitIORSclera"), Range(1.0f, 2.0f)]
		public float scleraIOR = IOR_HUMAN_TEARS;

		[Header("Cornea")]
		public bool corneaCrossSectionEditMode = false;
		[FormerlySerializedAs("eyeCorneaRadiusStart")]
		public float corneaCrossSection = 0.01325f;
		[FormerlySerializedAs("eyeIrisPlaneOffset")]
		public float corneaCrossSectionIrisOffset = 0.002f;
		[FormerlySerializedAs("eyeCorneaLimbusDarkeningOffset")]
		public float corneaCrossSectionFadeOffset = 0.00075f;
		[FormerlySerializedAs("eyeLitIORCornea"), Range(1.0f, 2.0f)]
		public float corneaIOR = IOR_HUMAN_CORNEA;
		[FormerlySerializedAs("eyeCorneaIndexOfRefraction"), Range(1.0f, 2.0f)]
		public float corneaIORIrisRay = 1.3f;
		[FormerlySerializedAs("eyeCorneaSmoothness"), Range(0.0f, 1.0f)]
		public float corneaSmoothness = 0.917f;
		[FormerlySerializedAs("eyeCorneaSSSScale"), Range(0.0f, 1.0f)]
		public float corneaSSS = 0.0f;

		[Header("Iris")]
		[FormerlySerializedAs("eyeIrisBentLighting")]
		public bool irisRefractedLighting = true;
		[Tooltip("Enables constant step (being the specified iris offset) along the refracted ray below the cornea cross section, rather than stepping all the way to the plane defined by the iris offset. Effectively curves the iris towards the refracted ray.")]
		public bool irisRefractedOffset = true;

		[Header("Pupil")]
		[FormerlySerializedAs("eyePupilOffset")]
		public Vector2 pupilUVOffset = new Vector2(0.002f, 0.016f);
		[FormerlySerializedAs("eyePupilDiameter")]
		public float pupilUVDiameter = 0.095f;
		[FormerlySerializedAs("eyePupilFalloff")]
		public float pupilUVFalloff = 0.015f;
		[FormerlySerializedAs("eyePupilScale"), Range(0.5f, 2.2f)]
		public float pupilScale = 1.0f;

		[Header("Occlusion")]
		[FormerlySerializedAs("eyeAsgPower"), Range(1e-1f, 128.0f)]
		public float asgPower = 10.0f;
		[FormerlySerializedAs("eyeAsgThreshold"), Range(1e-7f, 1e-1f)]
		public float asgThreshold = 1e-7f;
		[FormerlySerializedAs("eyeAsgModulateAlbedo"), Range(0.0f, 1.0f)]
		public float asgModulateAlbedo = 0.65f;
		[FormerlySerializedAs("eyePolygonContainer")]
		public Transform asgMarkerPolygon;

		public enum ConeMapping
		{
			ObjectSpaceMean,
			ClosingAxisSpaceSplit,
		}

		public struct ConeData
		{
			public Vector3 osMarkerL;
			public Vector3 osMarkerR;
			public Vector3 osMarkerT;
			public Vector3 osMarkerB;
			public Vector3 closingPlaneOrigin;
			public Vector3 closingPlanePosTop;
			public Vector3 closingPlanePosBottom;
			public Vector3 openingPosLeft;
			public Vector3 openingPosRight;
		}

		[Space]
		public ConeMapping coneMapping = ConeMapping.ClosingAxisSpaceSplit;
		[VisibleIf("coneMapping", ConeMapping.ClosingAxisSpaceSplit)]
		public float coneOriginOffset = 1.0f;
		[VisibleIf("coneMapping", ConeMapping.ClosingAxisSpaceSplit)]
		public Vector2 coneScale = Vector2.one;
		[VisibleIf("coneMapping", ConeMapping.ClosingAxisSpaceSplit)]
		public Vector3 coneBias = Vector3.zero;
		public bool coneDebug = false;
		private ConeData coneDebugData;

		[NonSerialized]
		public Vector3 asgOriginOS = new Vector3(0.0f, 0.0f, 0.0f);
		[NonSerialized]
		public Vector3 asgMeanOS = new Vector3(0.0f, 0.0f, 1.0f);
		[NonSerialized]
		public Vector3 asgTangentOS = new Vector3(1.0f, 0.0f, 0.0f);
		[NonSerialized]
		public Vector3 asgBitangentOS = new Vector3(0.0f, 1.0f, 0.0f);
		[NonSerialized]
		public Vector2 asgSharpness = new Vector2(1.25f, 9.0f);
		[NonSerialized]
		public Vector2 asgThresholdScaleBias = new Vector2(1.0f, 0.0f);

		void Awake()
		{
			rnd = GetComponent<Renderer>();
			rndProps = new MaterialPropertyBlock();
		}

		// https://mynameismjp.wordpress.com/2016/10/09/sg-series-part-2-spherical-gaussians-101/
		float AsgSharpnessFromThreshold(float epsilon, float amplitude, float power, float cosTheta)
		{
			// amplitude * e^(pow(sharpness * (cosTheta - 1), power)) = epsilon
			// e^(pow(sharpness * (cosTheta - 1), power)) = epsilon / amplitude
			// pow(sharpness * (cosTheta - 1), power) = log(epsilon / amplitude)
			// pow(sharpness * (cosTheta - 1), power) = log(epsilon) - log(amplitude)
			// sharpness * (cosTheta - 1) = pow(log(epsilon) - log(amplitude), 1.0 / power)
			// sharpness = pow(log(epsilon) - log(amplitude), 1.0 / power) / (cosTheta - 1)
			return 0.5f * Mathf.Pow(-Mathf.Log(epsilon) - Mathf.Log(amplitude), 1.0f / power) / -(cosTheta - 1.0f);
		}

		void AsgParameterUpdate()
		{
			Vector3 osMarkerL = (1.1f * geometryRadius) * Vector3.Normalize(Vector3.forward + Vector3.left);
			Vector3 osMarkerR = (1.1f * geometryRadius) * Vector3.Normalize(Vector3.forward + Vector3.right);
			Vector3 osMarkerT = (1.1f * geometryRadius) * Vector3.Normalize(Vector3.forward + 0.35f * Vector3.up);
			Vector3 osMarkerB = (1.1f * geometryRadius) * Vector3.Normalize(Vector3.forward + 0.35f * Vector3.down);

			if (asgMarkerPolygon != null && asgMarkerPolygon.childCount == 4)
			{
				//       1
				//   .-´   `-.
				// 0           2
				//   `-.   .-´
				//       3

				osMarkerL = this.transform.InverseTransformPoint(asgMarkerPolygon.GetChild(0).position);
				osMarkerR = this.transform.InverseTransformPoint(asgMarkerPolygon.GetChild(2).position);
				osMarkerT = this.transform.InverseTransformPoint(asgMarkerPolygon.GetChild(1).position);
				osMarkerB = this.transform.InverseTransformPoint(asgMarkerPolygon.GetChild(3).position);
			}

			coneDebugData.osMarkerL = osMarkerL;
			coneDebugData.osMarkerR = osMarkerR;
			coneDebugData.osMarkerT = osMarkerT;
			coneDebugData.osMarkerB = osMarkerB;

			float cosThetaTangent = 0.0f;
			float cosThetaBitangent = 0.0f;

			switch (coneMapping)
			{
				case ConeMapping.ObjectSpaceMean:
					{
						asgOriginOS = geometryOrigin;
						asgMeanOS = Vector3.Normalize(
							Vector3.Normalize(osMarkerT) +
							Vector3.Normalize(osMarkerR) +
							Vector3.Normalize(osMarkerB) +
							Vector3.Normalize(osMarkerL)
						);
						asgBitangentOS = Vector3.Cross(asgMeanOS, Vector3.Normalize(osMarkerR - osMarkerL));
						asgTangentOS = Vector3.Cross(asgBitangentOS, asgMeanOS);

						float cosThetaMeanToLeft = Vector3.Dot(Vector3.Normalize(osMarkerL), asgMeanOS);
						float cosThetaMeanToRight = Vector3.Dot(Vector3.Normalize(osMarkerR), asgMeanOS);
						float cosThetaMeanToTop = Vector3.Dot(Vector3.Normalize(osMarkerT), asgMeanOS);
						float cosThetaMeanToBottom = Vector3.Dot(Vector3.Normalize(osMarkerB), asgMeanOS);

						cosThetaTangent = (cosThetaMeanToLeft + cosThetaMeanToRight) * 0.5f;
						cosThetaBitangent = (cosThetaMeanToTop + cosThetaMeanToBottom) * 0.5f;
					}
					break;

				case ConeMapping.ClosingAxisSpaceSplit:
					{
						var asgPolygonRot = Quaternion.Euler(coneBias.y, coneBias.x, coneBias.z);
						osMarkerL = asgPolygonRot * osMarkerL;
						osMarkerR = asgPolygonRot * osMarkerR;
						osMarkerT = asgPolygonRot * osMarkerT;
						osMarkerB = asgPolygonRot * osMarkerB;

						var closingPlaneNormal = Vector3.Normalize(osMarkerR - osMarkerL);
						var closingPlaneOrigin = Vector3.ProjectOnPlane(osMarkerL, closingPlaneNormal);

						var closingPlanePosTop = Vector3.ProjectOnPlane(osMarkerT, closingPlaneNormal) - closingPlaneOrigin;
						var closingPlanePosBottom = Vector3.ProjectOnPlane(osMarkerB, closingPlaneNormal) - closingPlaneOrigin;
						var closingPlaneDirTop = Vector3.Normalize(closingPlanePosTop);
						var closingPlaneDirBottom = Vector3.Normalize(closingPlanePosBottom);

						var closingPlaneForward = Vector3.Normalize(closingPlaneDirTop + closingPlaneDirBottom);
						{
							closingPlaneOrigin -= closingPlaneForward * (0.01f * coneOriginOffset);
							//TODO pick an origin that sends the resulting forward vector through the original origin in the closing plane

							closingPlanePosTop = Vector3.ProjectOnPlane(osMarkerT, closingPlaneNormal) - closingPlaneOrigin;
							closingPlanePosBottom = Vector3.ProjectOnPlane(osMarkerB, closingPlaneNormal) - closingPlaneOrigin;
							closingPlaneDirTop = Vector3.Normalize(closingPlanePosTop);
							closingPlaneDirBottom = Vector3.Normalize(closingPlanePosBottom);

							closingPlaneForward = Vector3.Normalize(closingPlaneDirTop + closingPlaneDirBottom);
						}

						var openingPosLeft = (osMarkerL - closingPlaneOrigin);
						var openingPosRight = (osMarkerR - closingPlaneOrigin);
						var openingDirLeft = Vector3.Normalize(openingPosLeft);
						var openingDirRight = Vector3.Normalize(openingPosRight);

						var closingPlaneAltitude = coneScale.y * 0.5f * Mathf.Deg2Rad * Vector3.Angle(closingPlaneDirTop, closingPlaneDirBottom);
						var closingPlaneAzimuth = coneScale.x * 0.5f * Mathf.Deg2Rad * Vector3.Angle(openingDirLeft, openingDirRight);

						coneDebugData.closingPlaneOrigin = closingPlaneOrigin;
						coneDebugData.closingPlanePosTop = closingPlanePosTop;
						coneDebugData.closingPlanePosBottom = closingPlanePosBottom;
						coneDebugData.openingPosLeft = openingPosLeft;
						coneDebugData.openingPosRight = openingPosRight;

						asgOriginOS = closingPlaneOrigin;
						asgMeanOS = closingPlaneForward;
						asgTangentOS = closingPlaneNormal;
						asgBitangentOS = Vector3.Normalize(Vector3.Cross(asgMeanOS, asgTangentOS));

						cosThetaTangent = Mathf.Cos(closingPlaneAzimuth);
						cosThetaBitangent = Mathf.Cos(closingPlaneAltitude);
					}
					break;

			}// switch (coneMapping)

			asgSharpness.x = AsgSharpnessFromThreshold(asgThreshold, 1.0f, asgPower, cosThetaTangent);
			asgSharpness.y = AsgSharpnessFromThreshold(asgThreshold, 1.0f, asgPower, cosThetaBitangent);

			asgThresholdScaleBias.x = 1.0f / (1.0f - asgThreshold);
			asgThresholdScaleBias.y = -asgThreshold / (1.0f - asgThreshold);
		}

		void LateUpdate()
		{
			AsgParameterUpdate();

			if (rndProps == null)
				rndProps = new MaterialPropertyBlock();

			rnd.GetPropertyBlock(rndProps);
			{
				var geometryLookRotation = Quaternion.Euler(geometryAngle);

				rndProps.SetFloat("_EyeGeometryRadius", geometryRadius);
				rndProps.SetVector("_EyeGeometryOrigin", geometryOrigin);
				rndProps.SetVector("_EyeGeometryForward", Vector3.Normalize(geometryLookRotation * Vector3.forward));
				rndProps.SetVector("_EyeGeometryRight", Vector3.Normalize(geometryLookRotation * Vector3.right));
				rndProps.SetVector("_EyeGeometryUp", Vector3.Normalize(geometryLookRotation * Vector3.up));

				if (corneaCrossSectionEditMode && Application.isEditor)
				{
					rndProps.SetFloat("_EyeCorneaCrossSection", 1e+7f);
					rndProps.SetFloat("_EyeCorneaCrossSectionIrisOffset", 0.0f);
					rndProps.SetFloat("_EyeCorneaCrossSectionFadeOffset", 0.0f);
				}
				else
				{
					rndProps.SetFloat("_EyeCorneaCrossSection", corneaCrossSection);
					rndProps.SetFloat("_EyeCorneaCrossSectionIrisOffset", Mathf.Max(0.0f, corneaCrossSectionIrisOffset));
					rndProps.SetFloat("_EyeCorneaCrossSectionFadeOffset", Mathf.Max(0.0f, corneaCrossSectionFadeOffset));
				}

				rndProps.SetFloat("_EyeCorneaIOR", corneaIOR);
				rndProps.SetFloat("_EyeCorneaIORIrisRay", corneaIORIrisRay);
				rndProps.SetFloat("_EyeCorneaSmoothness", corneaSmoothness);
				rndProps.SetFloat("_EyeCorneaSSS", corneaSSS);

				rndProps.SetFloat("_EyeIrisRefractedLighting", irisRefractedLighting ? 1 : 0);
				rndProps.SetFloat("_EyeIrisRefractedOffset", irisRefractedOffset ? 1 : 0);

				rndProps.SetVector("_EyePupilUVOffset", pupilUVOffset);
				rndProps.SetFloat("_EyePupilUVDiameter", pupilUVDiameter);
				rndProps.SetFloat("_EyePupilUVFalloff", pupilUVFalloff);
				rndProps.SetFloat("_EyePupilScale", pupilScale);

				rndProps.SetFloat("_EyeScleraIOR", scleraIOR);

				rndProps.SetFloat("_EyeAsgPower", asgPower);
				rndProps.SetVector("_EyeAsgSharpness", asgSharpness);
				rndProps.SetVector("_EyeAsgThresholdScaleBias", asgThresholdScaleBias);
				rndProps.SetVector("_EyeAsgOriginOS", asgOriginOS);
				rndProps.SetVector("_EyeAsgMeanOS", asgMeanOS);
				rndProps.SetVector("_EyeAsgTangentOS", asgTangentOS);
				rndProps.SetVector("_EyeAsgBitangentOS", asgBitangentOS);
				rndProps.SetFloat("_EyeAsgModulateAlbedo", asgModulateAlbedo);
			}
			rnd.SetPropertyBlock(rndProps);
		}

		void OnDrawGizmos()
		{
			if (!coneDebug)
				return;

			var oldColor = Gizmos.color;
			var oldMatrix = Gizmos.matrix;
			{
				Gizmos.matrix = transform.localToWorldMatrix;

				// cone markers
				Gizmos.color = Color.white;
				Gizmos.DrawRay(Vector3.zero, coneDebugData.osMarkerT);
				Gizmos.DrawRay(Vector3.zero, coneDebugData.osMarkerR);
				Gizmos.DrawRay(Vector3.zero, coneDebugData.osMarkerB);
				Gizmos.DrawRay(Vector3.zero, coneDebugData.osMarkerL);

				// cone closing axis
				if (coneMapping == ConeMapping.ClosingAxisSpaceSplit)
				{
					Gizmos.color = Color.yellow;
					Gizmos.DrawRay(coneDebugData.closingPlaneOrigin, coneDebugData.closingPlanePosTop);
					Gizmos.DrawRay(coneDebugData.closingPlaneOrigin, coneDebugData.closingPlanePosBottom);
					Gizmos.DrawRay(coneDebugData.closingPlaneOrigin, coneDebugData.openingPosLeft);
					Gizmos.DrawRay(coneDebugData.closingPlaneOrigin, coneDebugData.openingPosRight);
				}

				// asg frame
				Gizmos.color = Color.Lerp(Color.yellow, Color.red, 0.5f);
				Gizmos.DrawRay(asgOriginOS, (1.5f * geometryRadius) * asgMeanOS);
				Gizmos.DrawRay(asgOriginOS, (1.5f * geometryRadius) * asgBitangentOS);
				Gizmos.DrawRay(asgOriginOS, (1.5f * geometryRadius) * asgTangentOS);
			}

			Gizmos.color = oldColor;
			Gizmos.matrix = oldMatrix;
		}
	}
}
