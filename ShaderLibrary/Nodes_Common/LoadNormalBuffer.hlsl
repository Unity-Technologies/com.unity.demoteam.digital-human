#ifndef __LOADNORMALBUFFER_H__
#define __LOADNORMALBUFFER_H__

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/TextureXR.hlsl"
#ifndef UNITY_COMMON_MATERIAL_INCLUDED// in 7.3.1 there is no include guard in NormalBuffer.hlsl
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
#endif

void LoadNormalBuffer_float(in float2 positionSS, out float3 normalWS, out float smoothness)
{
#if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_FORWARD)
	NormalData normalData;
	DecodeFromNormalBuffer(positionSS.xy, normalData);
	normalWS = normalData.normalWS;
	smoothness = 1.0 - normalData.perceptualRoughness;
#else
	normalWS = float3(0, 0, 1);
	smoothness = 0.5;
#endif
}

#endif//__LOADNORMALBUFFER_H__