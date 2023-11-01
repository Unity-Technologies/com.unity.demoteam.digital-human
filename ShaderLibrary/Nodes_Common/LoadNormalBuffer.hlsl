#ifndef __LOADNORMALBUFFER_H__
#define __LOADNORMALBUFFER_H__

//#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/TextureXR.hlsl"
//#ifndef UNITY_COMMON_MATERIAL_INCLUDED// in 7.3.1 there is no include guard in NormalBuffer.hlsl
//#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
//#endif

//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

#if defined(UNITY_STEREO_INSTANCING_ENABLED)
	#define SLICE_ARRAY_INDEX   unity_StereoEyeIndex
#else
	#define SLICE_ARRAY_INDEX  0
#endif

struct NormalData
{
	float3 normalWS;
	float  perceptualRoughness;
};

#define TEXTURE2D_ARRAY(textureName)          Texture2DArray textureName
#define TEXTURE2D_X                           TEXTURE2D_ARRAY
#define LOAD_TEXTURE2D_ARRAY(textureName, unCoord2, index)                      textureName.Load(int4(unCoord2, index, 0))

#define LOAD_TEXTURE2D_X(textureName, unCoord2) LOAD_TEXTURE2D_ARRAY(textureName, unCoord2, SLICE_ARRAY_INDEX)

TEXTURE2D_X(_NormalBufferTexture);

void DecodeFromNormalBuffer(float4 normalBuffer, out NormalData normalData)
{
	float3 packNormalWS = normalBuffer.rgb;
	float2 octNormalWS = Unpack888ToFloat2(packNormalWS);
	normalData.normalWS = UnpackNormalOctQuadEncode(octNormalWS * 2.0 - 1.0);
	normalData.perceptualRoughness = normalBuffer.a;
}

void DecodeFromNormalBuffer(uint2 positionSS, out NormalData normalData)
{
	float4 normalBuffer = LOAD_TEXTURE2D_X(_NormalBufferTexture, positionSS);
	DecodeFromNormalBuffer(normalBuffer, normalData);
}

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
