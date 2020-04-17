#ifndef __EYEPROPERTIES_H__
#define __EYEPROPERTIES_H__

float _EyeGeometryRadius;
float3 _EyeGeometryOrigin;
float3 _EyeGeometryForward;
float3 _EyeGeometryRight;
float3 _EyeGeometryUp;

float _EyeScleraIOR;

float _EyeCorneaCrossSection;
float _EyeCorneaCrossSectionIrisOffset;
float _EyeCorneaCrossSectionFadeOffset;
float _EyeCorneaIOR;
float _EyeCorneaIORIrisRay;
float _EyeCorneaSmoothness;
float _EyeCorneaSSS;

float _EyeIrisRefractedLighting;
float _EyeIrisRefractedOffset;

float2 _EyePupilUVOffset;
float _EyePupilUVDiameter;
float _EyePupilUVFalloff;
float _EyePupilScale;

float _EyeAsgPower;
float _EyeAsgModulateAlbedo;

float3 _EyeAsgOriginOS;
float3 _EyeAsgMeanOS;
float3 _EyeAsgTangentOS;
float3 _EyeAsgBitangentOS;
float2 _EyeAsgSharpness;
float2 _EyeAsgThresholdScaleBias;

struct AnisotropicSphericalSuperGaussian
{
	// (Anisotropic) Higher-Order Gaussian Distribution aka (Anisotropic) Super-Gaussian Distribution extended to be evaluated across the unit sphere.
	//
	// Source for Super-Gaussian Distribution:
	// https://en.wikipedia.org/wiki/Gaussian_function#Higher-order_Gaussian_or_super-Gaussian_function
	//
	// Source for Anisotropic Spherical Gaussian Distribution:
	// http://www.jp.square-enix.com/info/library/pdf/Virtual%20Spherical%20Gaussian%20Lights%20for%20Real-Time%20Glossy%20Indirect%20Illumination%20(supplemental%20material).pdf
	//
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

struct EyeProperties
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

float2 ResolvePlanarUV(in float3 positionOS)
{
	float3 r = (positionOS - _EyeGeometryOrigin) / (2.0 * _EyeGeometryRadius);
	float3 rp = r - _EyeGeometryForward * dot(_EyeGeometryForward, r);
	return float2(
		0.5 + dot(rp, _EyeGeometryRight),
		0.5 + dot(rp, _EyeGeometryUp)
	);
}

EyeProperties ResolveEyeProperties(in float3 surfacePositionOS, in float3 surfaceNormalOS)
{
	float3 viewPositionOS = TransformWorldToObject(GetCameraRelativePositionWS(_WorldSpaceCameraPos));
	float3 viewDirectionOS = normalize(surfacePositionOS - viewPositionOS);

	// prepare texture coordinates for top and bottom layer
	float2 uv0 = ResolvePlanarUV(surfacePositionOS);
	float2 uv1 = uv0;

	// if the surface is above the cornea cross-section, then we refract the view ray and intersect
	// this new ray with the iris, in order to obtain the texture coordinates for the bottom layer
	float3 crossSectionOrigin = _EyeGeometryOrigin + _EyeGeometryForward * _EyeCorneaCrossSection;
	float crossSectionDistance = dot(_EyeGeometryForward, surfacePositionOS - crossSectionOrigin);

	if (crossSectionDistance > 0.0)
	{
		float3 refractedRayOS = refract(viewDirectionOS, surfaceNormalOS, 1.0 / _EyeCorneaIORIrisRay);
		float refractedRayCosA = -dot(_EyeGeometryForward, refractedRayOS);
		float refractedRayT;
		{
			if (_EyeIrisRefractedOffset)
				refractedRayT = (crossSectionDistance / refractedRayCosA) + _EyeCorneaCrossSectionIrisOffset;
			else
				refractedRayT = (crossSectionDistance + _EyeCorneaCrossSectionIrisOffset) / refractedRayCosA;
		}

		uv1 = ResolvePlanarUV(surfacePositionOS + refractedRayOS * refractedRayT);
	}

	// perform linear uv remapping of the bottom layer to allow dilation of the pupil
	if (_EyePupilScale != 1.0)
	{
		float2 centerPos = float2(0.5, 0.5) - _EyePupilUVOffset;
		float2 centerVec = uv1 - centerPos;
		float centerDist = length(centerVec);
		float2 centerDir = centerVec / centerDist;

		float sampleIrisMax = 2.2 * _EyePupilUVDiameter;//TODO replace with user param
		float sampleIrisMin = 0.5 * _EyePupilUVDiameter;

		float outputIrisMax = sampleIrisMax;
		float outputIrisMin = clamp(sampleIrisMin * _EyePupilScale, FLT_EPS, sampleIrisMax - FLT_EPS);
		float outputIrisPos = saturate((centerDist - outputIrisMin) / (outputIrisMax - outputIrisMin));

		uv1 = centerPos + centerDir * lerp(sampleIrisMin, sampleIrisMax, outputIrisPos);
	}

	// resolve blend masks for sclera and cornea
	float maskSclera = smoothstep(0.0, _EyeCorneaCrossSectionFadeOffset, -crossSectionDistance);
	float maskCornea = smoothstep(0.0, _EyeCorneaCrossSectionFadeOffset, crossSectionDistance);

	// resolve blend mask for the pupil
	float distPupil = length(uv1 - float2(0.5, 0.5) + _EyePupilUVOffset) - (0.5 * _EyePupilUVDiameter);
	float maskPupil = smoothstep(_EyePupilUVFalloff, 0.0, distPupil);

	// construct a function across the surface of the eye that roughly approximates visiblity function (eye lids occlude eye),
	// but are too thin / small to rely on typical shadow mapping techniques to capture high enough quality visiblity
	AnisotropicSphericalSuperGaussian asg;
	asg.amplitude = 1.0;
	asg.power = _EyeAsgPower;
	asg.sharpness = _EyeAsgSharpness;
	asg.mean = _EyeAsgMeanOS;
	asg.tangent = _EyeAsgTangentOS;
	asg.bitangent = _EyeAsgBitangentOS;

	float3 asgEvaluationDirectionOS = normalize(surfacePositionOS - _EyeAsgOriginOS);
	float asgAO = EvaluateAnisotropicSphericalSuperGaussian(asg, asgEvaluationDirectionOS);

	// copy to output struct
	EyeProperties props;
	props.refractedUV = lerp(uv0, uv1, maskCornea);
	props.maskCornea = maskCornea;
	props.maskSclera = maskSclera;
	props.maskPupil = maskPupil;
	props.asgAO = saturate(asgAO * _EyeAsgThresholdScaleBias.x + _EyeAsgThresholdScaleBias.y);
	props.asgModulateAlbedo = _EyeAsgModulateAlbedo;
	props.ior = lerp(_EyeScleraIOR, _EyeCorneaIOR, maskCornea);
	props.irisBentLighting = _EyeIrisRefractedLighting;
	props.corneaSmoothness = _EyeCorneaSmoothness;
	props.corneaSSS = _EyeCorneaSSS;
	return props;
}

void EyeProperties_float(in float3 positionOS, in float3 normalOS, out float2 refractedUV, out float maskCornea, out float maskSclera, out float maskPupil, out float asgAO, out float asgModulateAlbedo, out float ior, out float irisBentLighting, out float corneaSmoothness, out float corneaSSS)
{
	EyeProperties props = ResolveEyeProperties(positionOS, normalOS);
	{
		refractedUV = props.refractedUV;
		maskCornea = props.maskCornea;
		maskSclera = props.maskSclera;
		maskPupil = props.maskPupil;
		asgAO = props.asgAO;
		asgModulateAlbedo = props.asgModulateAlbedo;
		ior = props.ior;
		irisBentLighting = props.irisBentLighting;
		corneaSmoothness = props.corneaSmoothness;
		corneaSSS = props.corneaSSS;
	}
}

#endif//__EYEPROPERTIES_H__
