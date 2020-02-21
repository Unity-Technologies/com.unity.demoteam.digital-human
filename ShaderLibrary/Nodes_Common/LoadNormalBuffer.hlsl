#ifndef __LOADNORMALBUFFER_H__
#define __LOADNORMALBUFFER_H__

#if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_FORWARD)
	void LoadNormalBuffer_float(in float2 positionSS, out float3 normalWS, out float smoothness)
	{
		NormalData normalData;
		DecodeFromNormalBuffer(positionSS.xy, normalData);
		normalWS = normalData.normalWS;
		smoothness = 1.0 - normalData.perceptualRoughness;
	}
#else
	void LoadNormalBuffer_float(in float2 positionSS, out float3 normalWS, out float smoothness)
	{
		normalWS = float3(0, 0, 1);
		smoothness = 0.5;
	}
#endif

#endif//__LOADNORMALBUFFER_H__