using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
	[CustomEditor(typeof(EyeRenderer))]
	public class EyeRendererEditor : Editor
	{
		void OnSceneGUI()
		{
			var eye = target as EyeRenderer;
			if (eye == null)
				return;

			DrawEyeHandles(eye);
		}

		void DrawEyeHandles(EyeRenderer eye)
		{
			var geometryRadius = eye.geometryRadius;
			var geometryOrigin = eye.geometryOrigin;
			var geometryDiameter = 2.0f * geometryRadius;
			var geometryLookRadius = 1.2f * geometryRadius;
			var geometryLookRotation = Quaternion.Euler(eye.geometryAngle);

			var geometryForward = Vector3.Normalize(geometryLookRotation * Vector3.forward);
			var geometryRight = Vector3.Normalize(geometryLookRotation * Vector3.right);
			var geometryUp = Vector3.Normalize(geometryLookRotation * Vector3.up);

			var drawColorWire = Color.Lerp(Color.clear, Color.green, 1.0f);
			var drawColorSolid = Color.Lerp(Color.clear, Color.green, 0.6f);

			var drawMatrixObject = eye.transform.localToWorldMatrix;
			var drawMatrixGeometry = drawMatrixObject * Matrix4x4.TRS(geometryOrigin, geometryLookRotation, Vector3.one);

			var crossSection = eye.corneaCrossSection;
			var crossSectionIris = crossSection - eye.corneaCrossSectionIrisOffset;
			var crossSectionFade = crossSection + eye.corneaCrossSectionFadeOffset;

			var pupilOrigin = geometryDiameter * new Vector3(-eye.pupilUVOffset.x, -eye.pupilUVOffset.y, 0.0f) + Vector3.forward * crossSectionIris;
			var pupilRadius = geometryDiameter * (0.5f * eye.pupilUVDiameter);

			Vector3 hndGeometryOrigin = geometryOrigin;
			Vector3 hndGeometryRadius = geometryOrigin + geometryRight * geometryRadius;
			Vector3 hndGeometryForward = geometryOrigin + geometryForward * geometryLookRadius;

			Vector3 hndCrossSection = Vector3.forward * crossSection;
			Vector3 hndCrossSectionIris = Vector3.forward * crossSectionIris;
			Vector3 hndCrossSectionFade = Vector3.forward * crossSectionFade;

			Vector3 hndPupilOrigin = pupilOrigin;
			Vector3 hndPupilRadius = pupilOrigin + Vector3.right * pupilRadius;

			bool hndGeometryRadiusChanged = false;
			bool hndCrossSectionChanged = false;
			bool hndCrossSectionIrisChanged = false;
			bool hndCrossSectionFadeChanged = false;
			bool hndPupilOriginChanged = false;
			bool hndPupilRadiusChanged = false;

			float hndSize = geometryRadius / 50.0f;
			float hndDots = 2.0f;

			EditorGUI.BeginChangeCheck();

			using (new Handles.DrawingScope(drawColorWire, drawMatrixObject))
			{
				// geometry
				{
					Handles.DrawWireDisc(geometryOrigin, geometryForward, geometryRadius);

					Handles.DrawDottedLine(geometryOrigin, geometryOrigin + geometryForward * geometryLookRadius, hndDots);
					Handles.DrawDottedLine(geometryOrigin, geometryOrigin + geometryRight * geometryRadius, hndDots);
					Handles.DrawDottedLine(geometryOrigin, geometryOrigin + geometryRight * -geometryRadius, hndDots);
					Handles.DrawDottedLine(geometryOrigin, geometryOrigin + geometryUp * geometryRadius, hndDots);
					Handles.DrawDottedLine(geometryOrigin, geometryOrigin + geometryUp * -geometryRadius, hndDots);

					Handles.DrawWireArc(geometryOrigin, geometryUp, geometryForward, 12.0f, geometryLookRadius);
					Handles.DrawWireArc(geometryOrigin, geometryUp, geometryForward, -12.0f, geometryLookRadius);
					Handles.DrawWireArc(geometryOrigin, geometryRight, geometryForward, 12.0f, geometryLookRadius);
					Handles.DrawWireArc(geometryOrigin, geometryRight, geometryForward, -12.0f, geometryLookRadius);

					using (var check = new EditorGUI.ChangeCheckScope())
					{
						hndGeometryRadius = Handles.Slider2D(hndGeometryRadius, geometryForward, geometryRight, geometryUp, hndSize, Handles.SphereHandleCap, 0.0f);
						hndGeometryRadiusChanged = check.changed;
					}
				}

				// pupil
				using (new Handles.DrawingScope(drawMatrixGeometry))
				{
					Handles.DrawDottedLine(hndPupilOrigin, hndPupilRadius, hndDots);
					Handles.DrawWireDisc(hndPupilOrigin, Vector3.forward, pupilRadius);

					using (var check = new EditorGUI.ChangeCheckScope())
					{
						hndPupilOrigin = Handles.Slider2D(hndPupilOrigin, Vector3.forward, Vector3.right, Vector3.up, pupilRadius, Handles.CircleHandleCap, 0.0f);
						hndPupilOriginChanged = check.changed;
					}

					using (var check = new EditorGUI.ChangeCheckScope())
					{
						hndPupilRadius = Handles.Slider2D(hndPupilRadius, Vector3.forward, Vector3.right, Vector3.up, hndSize, Handles.SphereHandleCap, 0.0f);
						hndPupilRadiusChanged = check.changed;
					}
				}

				// cross section
				using (new Handles.DrawingScope(drawMatrixGeometry))
				{
					var dir45A = Vector3.Normalize(Vector3.right + Vector3.up);
					var dir45B = Vector3.Normalize(Vector3.right - Vector3.up);

					var extCrossSection = geometryRadius * 0.65f;
					var extCrossSectionFadeOffset = geometryRadius * 0.5f;
					var extCrossSectionIrisOffset = geometryRadius * 0.35f;

					using (new Handles.DrawingScope(new Color(1.0f, 0.5f, 0.0f)))
					{
						Handles.DrawLine(hndCrossSectionIris - dir45A * extCrossSectionIrisOffset, hndCrossSectionIris + dir45A * extCrossSectionIrisOffset);
						Handles.DrawLine(hndCrossSectionIris - dir45B * extCrossSectionIrisOffset, hndCrossSectionIris + dir45B * extCrossSectionIrisOffset);

						Handles.DrawLine(hndCrossSectionIris - dir45A * extCrossSectionIrisOffset, hndCrossSection - dir45A * extCrossSectionIrisOffset);
						Handles.DrawLine(hndCrossSectionIris - dir45B * extCrossSectionIrisOffset, hndCrossSection - dir45B * extCrossSectionIrisOffset);

						Handles.DrawLine(hndCrossSectionIris + dir45A * extCrossSectionIrisOffset, hndCrossSection + dir45A * extCrossSectionIrisOffset);
						Handles.DrawLine(hndCrossSectionIris + dir45B * extCrossSectionIrisOffset, hndCrossSection + dir45B * extCrossSectionIrisOffset);

						using (var check = new EditorGUI.ChangeCheckScope())
						{
							hndCrossSectionIris = Handles.Slider2D(hndCrossSectionIris, Vector3.right, Vector3.forward, Vector3.up, hndSize, Handles.CubeHandleCap, 0.0f);
							hndCrossSectionIrisChanged = check.changed;
						}
					}

					using (new Handles.DrawingScope(Color.red))
					{
						Handles.DrawLine(hndCrossSection - dir45A * extCrossSection, hndCrossSection + dir45A * extCrossSection);
						Handles.DrawLine(hndCrossSection - dir45B * extCrossSection, hndCrossSection + dir45B * extCrossSection);

						using (var check = new EditorGUI.ChangeCheckScope())
						{
							hndCrossSection = Handles.Slider2D(hndCrossSection, Vector3.right, Vector3.forward, Vector3.up, hndSize, Handles.CubeHandleCap, 0.0f);
							hndCrossSectionChanged = check.changed;
						}
					}

					using (new Handles.DrawingScope(new Color(1.0f, 0.0f, 0.5f)))
					{
						Handles.DrawLine(hndCrossSectionFade - dir45A * extCrossSectionFadeOffset, hndCrossSectionFade + dir45A * extCrossSectionFadeOffset);
						Handles.DrawLine(hndCrossSectionFade - dir45B * extCrossSectionFadeOffset, hndCrossSectionFade + dir45B * extCrossSectionFadeOffset);

						Handles.DrawLine(hndCrossSectionFade - dir45A * extCrossSectionFadeOffset, hndCrossSection - dir45A * extCrossSectionFadeOffset);
						Handles.DrawLine(hndCrossSectionFade - dir45B * extCrossSectionFadeOffset, hndCrossSection - dir45B * extCrossSectionFadeOffset);

						Handles.DrawLine(hndCrossSectionFade + dir45A * extCrossSectionFadeOffset, hndCrossSection + dir45A * extCrossSectionFadeOffset);
						Handles.DrawLine(hndCrossSectionFade + dir45B * extCrossSectionFadeOffset, hndCrossSection + dir45B * extCrossSectionFadeOffset);

						using (var check = new EditorGUI.ChangeCheckScope())
						{
							hndCrossSectionFade = Handles.Slider2D(hndCrossSectionFade, Vector3.right, Vector3.forward, Vector3.up, hndSize, Handles.CubeHandleCap, 0.0f);
							hndCrossSectionFadeChanged = check.changed;
						}
					}
				}
			}

			if (EditorGUI.EndChangeCheck())
			{
				Undo.RegisterCompleteObjectUndo(eye, "Eye property change");

				// update geometry
				if (hndGeometryRadiusChanged)
					eye.geometryRadius = Vector3.Magnitude(hndGeometryRadius - geometryOrigin);

				// update pupil
				if (hndPupilOriginChanged)
					eye.pupilUVOffset = new Vector2(-hndPupilOrigin.x, -hndPupilOrigin.y) / geometryDiameter;

				if (hndPupilRadiusChanged)
					eye.pupilUVDiameter = 2.0f * Vector3.Magnitude(hndPupilRadius - pupilOrigin) / geometryDiameter;

				// update cross section
				if (hndCrossSectionChanged)
					eye.corneaCrossSection = hndCrossSection.z;

				if (hndCrossSectionIrisChanged)
					eye.corneaCrossSectionIrisOffset = Mathf.Max(0.0f, crossSection - hndCrossSectionIris.z);

				if (hndCrossSectionFadeChanged)
					eye.corneaCrossSectionFadeOffset = Mathf.Max(0.0f, hndCrossSectionFade.z - crossSection);
			}
		}
	}
}
