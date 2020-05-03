#ifndef __SKINDEFORMATIONBLEND_HLSL__
#define __SKINDEFORMATIONBLEND_HLSL__

SamplerState SKINDEFORMATION_sampler_linear_repeat;
#define SKINDEFORMATION_SAMPLER SKINDEFORMATION_sampler_linear_repeat

TEXTURE2D(_BlendInput0_FrameAlbedoLo);
TEXTURE2D(_BlendInput0_FrameAlbedoHi);
float _BlendInput0_FrameFraction;
float _BlendInput0_ClipWeight;

TEXTURE2D(_BlendInput1_FrameAlbedoLo);
TEXTURE2D(_BlendInput1_FrameAlbedoHi);
float _BlendInput1_FrameFraction;
float _BlendInput1_ClipWeight;

void SkinDeformationBlend_float(in float2 uv, in float3 inAlbedo, out float3 outAlbedo)
{
	float3 blendInputsAlbedo = float3(0.0, 0.0, 0.0);
	float blendInputsWeight = 0.0;

	if (_BlendInput0_ClipWeight > 0.0)
	{
		float3 frameAlbedoLo = _BlendInput0_FrameAlbedoLo.Sample(SKINDEFORMATION_SAMPLER, uv).xyz;
		float3 frameAlbedoHi = _BlendInput0_FrameAlbedoHi.Sample(SKINDEFORMATION_SAMPLER, uv).xyz;
		blendInputsAlbedo += _BlendInput0_ClipWeight * lerp(frameAlbedoLo, frameAlbedoHi, _BlendInput0_FrameFraction);
		blendInputsWeight += _BlendInput0_ClipWeight;
	}

	if (_BlendInput1_ClipWeight > 0.0)
	{
		float3 frameAlbedoLo = _BlendInput1_FrameAlbedoLo.Sample(SKINDEFORMATION_SAMPLER, uv).xyz;
		float3 frameAlbedoHi = _BlendInput1_FrameAlbedoHi.Sample(SKINDEFORMATION_SAMPLER, uv).xyz;
		blendInputsAlbedo += _BlendInput1_ClipWeight * lerp(frameAlbedoLo, frameAlbedoHi, _BlendInput1_FrameFraction);
		blendInputsWeight += _BlendInput1_ClipWeight;
	}

	outAlbedo = lerp(inAlbedo, blendInputsAlbedo, saturate(blendInputsWeight));
}

#endif//__SKINDEFORMATIONBLEND_HLSL__
