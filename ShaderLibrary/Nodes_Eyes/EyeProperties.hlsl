#ifndef __EYEPROPERTIES_H__
#define __EYEPROPERTIES_H__

float _EyeRadius;

float _EyeCorneaRadiusStart;
float _EyeCorneaLimbusDarkeningOffset;
float _EyeCorneaIndexOfRefraction;
float _EyeCorneaIndexOfRefractionRatio;
float _EyeCorneaSSS;
float _EyeCorneaSmoothness;

float _EyeIrisBentLightingDebug;
float _EyeIrisBentLighting;
float _EyeIrisPlaneOffset;
float _EyeIrisConcavity;

float2 _EyePupilOffset;
float _EyePupilDiameter;
float _EyePupilFalloff;
float _EyePupilScale;

float _EyeWrappedLightingCosine;
float _EyeWrappedLightingPower;

float _EyeLitIORCornea;
float _EyeLitIORSclera;

float _EyeAsgPower;
float2 _EyeAsgSharpness;
float3 _EyeAsgOriginOS;
float3 _EyeAsgMeanOS;
float3 _EyeAsgTangentOS;
float3 _EyeAsgBitangentOS;
float2 _EyeAsgThresholdScaleBias;
float _EyeAsgModulateAlbedo;

// (Anisotropic) Higher-Order Gaussian Distribution aka (Anisotropic) Super-Gaussian Distribution extended to be evaluated across the unit sphere.
//
// Source for Super-Gaussian Distribution:
// https://en.wikipedia.org/wiki/Gaussian_function#Higher-order_Gaussian_or_super-Gaussian_function
//
// Source for Anisotropic Spherical Gaussian Distribution:
// http://www.jp.square-enix.com/info/library/pdf/Virtual%20Spherical%20Gaussian%20Lights%20for%20Real-Time%20Glossy%20Indirect%20Illumination%20(supplemental%20material).pdf
//
struct AnisotropicSphericalSuperGaussian
{
	float amplitude;
	float2 sharpness;
	float power;
	float3 mean;
	float3 tangent;
	float3 bitangent;
};

float EvaluateAnisotropicSphericalSuperGaussian(const in AnisotropicSphericalSuperGaussian a, const in float3 direction)
{
	float dDotM = dot(direction, a.mean);
	float dDotT = dot(direction, a.tangent);
	float dDotB = dot(direction, a.bitangent);
	return a.amplitude * max(0.0, dDotM) * exp(-pow(abs(-a.sharpness.x * dDotT * dDotT - a.sharpness.y * dDotB * dDotB), a.power));
}

struct EyeData
{
	float2 refractedUV;
	float maskCornea;
	float maskSclera;
	float maskPupil;
	float asgAO;
	float asgModulateAlbedo;
	float ior;
	float irisBentLighting;
	float corneaSmoothness;
	float corneaSSS;
};

EyeData GetEyeData(in float3 surfacePositionOS, in float3 surfaceNormalOS)
{
	float3 viewPositionOS = TransformWorldToObject(GetCameraRelativePositionWS(_WorldSpaceCameraPos));
	float3 viewDirectionOS = normalize(surfacePositionOS - viewPositionOS);

	// Eye geometry must be centered at origin in object space, planar uv mapped along Z, and look straight down +Z.
	const float3 irisPlaneNormalOS = float3(0.0, 0.0, 1.0);
	const float3 irisPlaneOriginOS = irisPlaneNormalOS * _EyeCorneaRadiusStart;

	// Currently using a user defined scale factor to go from object space to [0, 1] uv range, since our eye geometry
	// is not normalized radius in object space.
	const float2 uvFromOSScale = float2(-0.5, 0.5) / _EyeRadius;
	const float2 uvFromOSBias = float2(0.5, 0.5);

	// Calculate texture coordinates for top and bottom layer.
	float2 uv0 = surfacePositionOS.xy * uvFromOSScale + uvFromOSBias;
	float2 uv1 = uv0;

	// If eye geometry is passing into the positive half-space of our iris plane, it must be the cornea geometry.
	// Refract view ray based on human cornea index of refraction, and intersect it with the iris plane.
	float irisPlaneDistance = dot(irisPlaneNormalOS, surfacePositionOS - irisPlaneOriginOS);
	if (irisPlaneDistance > 0.0)
	{
		float3 refractedViewDirectionOS = refract(viewDirectionOS, surfaceNormalOS, _EyeCorneaIndexOfRefractionRatio);

		float cosA = -dot(irisPlaneNormalOS, refractedViewDirectionOS);
		float irisDistance = (irisPlaneDistance / cosA) + _EyeIrisPlaneOffset;
		float3 irisPositionOS = surfacePositionOS + irisDistance * refractedViewDirectionOS;

		uv1 = irisPositionOS.xy * uvFromOSScale + uvFromOSBias;
	}

	// Linear remapping of the iris to allow dilation of the pupil.
	if (_EyePupilScale != 1.0)
	{
		float2 centerPos = float2(0.5, 0.5) - _EyePupilOffset;
		float2 centerVec = uv1 - centerPos;
		float centerDist = length(centerVec);
		float2 centerDir = centerVec / centerDist;

		float sampleIrisMax = 2.2 * _EyePupilDiameter;//TODO replace with user param
		float sampleIrisMin = 0.5 * _EyePupilDiameter;

		float outputIrisMax = sampleIrisMax;
		float outputIrisMin = clamp(sampleIrisMin * _EyePupilScale, FLT_EPS, sampleIrisMax - FLT_EPS);
		float outputIrisPos = saturate((centerDist - outputIrisMin) / (outputIrisMax - outputIrisMin));

		uv1 = centerPos + centerDir * lerp(sampleIrisMin, sampleIrisMax, outputIrisPos);
	}

	// Prepare blend masks for cornea and sclera.
	float maskCornea = smoothstep(0.0, _EyeCorneaLimbusDarkeningOffset, irisPlaneDistance);
	float maskSclera = smoothstep(0.0, _EyeCorneaLimbusDarkeningOffset, -irisPlaneDistance);

	// Prepare blend mask for the pupil.
	float distPupil = length(uv1 - float2(0.5, 0.5) + _EyePupilOffset) - (0.5 * _EyePupilDiameter);
	float maskPupil = smoothstep(_EyePupilFalloff, 0.0, distPupil);

	// Construct a function across the surface of the eye that roughly approximates visiblity function (eye lids occlude eye),
	// but are too thin / small to rely on typical shadow mapping techniques to capture high enough quality visiblity.
	AnisotropicSphericalSuperGaussian a;
	a.amplitude = 1.0;
	a.power = _EyeAsgPower;
	a.sharpness = _EyeAsgSharpness;
	a.mean = _EyeAsgMeanOS;
	a.tangent = _EyeAsgTangentOS;
	a.bitangent = _EyeAsgBitangentOS;

	float3 asgEvaluationDirectionOS = normalize(surfacePositionOS - _EyeAsgOriginOS);
	float asgAO = EvaluateAnisotropicSphericalSuperGaussian(a, asgEvaluationDirectionOS);

	// Copy to output.
	EyeData eyeData;
	eyeData.refractedUV = lerp(uv0, uv1, maskCornea);
	eyeData.maskCornea = maskCornea;
	eyeData.maskSclera = maskSclera;
	eyeData.maskPupil = maskPupil;
	eyeData.asgAO = saturate(asgAO * _EyeAsgThresholdScaleBias.x + _EyeAsgThresholdScaleBias.y);
	eyeData.asgModulateAlbedo = _EyeAsgModulateAlbedo;
	eyeData.ior = lerp(_EyeLitIORSclera, _EyeLitIORCornea, maskCornea);
	eyeData.irisBentLighting = _EyeIrisBentLighting;
	eyeData.corneaSmoothness = _EyeCorneaSmoothness;
	eyeData.corneaSSS = _EyeCorneaSSS;
	return eyeData;
}

void EyeProperties_float(in float3 positionOS, in float3 normalOS, out float2 refractedUV, out float maskCornea, out float maskSclera, out float maskPupil, out float asgAO, out float asgModulateAlbedo, out float ior, out float irisBentLighting, out float corneaSmoothness, out float corneaSSS)
{
	EyeData data = GetEyeData(positionOS, normalOS);
	refractedUV = data.refractedUV;
	maskCornea = data.maskCornea;
	maskSclera = data.maskSclera;
	maskPupil = data.maskPupil;
	asgAO = data.asgAO;
	asgModulateAlbedo = data.asgModulateAlbedo;
	ior = data.ior;
	irisBentLighting = data.irisBentLighting;
	corneaSmoothness = data.corneaSmoothness;
	corneaSSS = data.corneaSSS;
}

#endif//__EYEPROPERTIES_H__
