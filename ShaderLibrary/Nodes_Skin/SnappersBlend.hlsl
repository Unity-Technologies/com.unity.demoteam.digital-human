#ifndef __SNAPPERSBLEND_HLSL__
#define __SNAPPERSBLEND_HLSL__

SamplerState SNAPPERS_sampler_linear_repeat;
#define SNAPPERS_SAMPLER SNAPPERS_sampler_linear_repeat

//#define _SNAPPERS_TEXTURE_ARRAYS
#ifdef _SNAPPERS_TEXTURE_ARRAYS
TEXTURE2D_ARRAY(_SnappersMask);// [0, ..., 11]
TEXTURE2D_ARRAY(_SnappersAlbedo);// [0, ..., 3] 
TEXTURE2D_ARRAY(_SnappersNormal);// [0, ..., 3] 
TEXTURE2D_ARRAY(_SnappersCavity);// [0, ..., 3] 
#else
TEXTURE2D(_SnappersMask1);
TEXTURE2D(_SnappersMask2);
TEXTURE2D(_SnappersMask3);
TEXTURE2D(_SnappersMask4);
TEXTURE2D(_SnappersMask5);
TEXTURE2D(_SnappersMask6);
TEXTURE2D(_SnappersMask7);
TEXTURE2D(_SnappersMask8);
TEXTURE2D(_SnappersMask9);
TEXTURE2D(_SnappersMask10);
TEXTURE2D(_SnappersMask11);
TEXTURE2D(_SnappersMask12);
TEXTURE2D(_SnappersAlbedo1);
TEXTURE2D(_SnappersAlbedo2);
TEXTURE2D(_SnappersAlbedo3);
TEXTURE2D(_SnappersAlbedo4);
TEXTURE2D(_SnappersNormal1);
TEXTURE2D(_SnappersNormal2);
TEXTURE2D(_SnappersNormal3);
TEXTURE2D(_SnappersNormal4);
TEXTURE2D(_SnappersCavity1);
TEXTURE2D(_SnappersCavity2);
TEXTURE2D(_SnappersCavity3);
TEXTURE2D(_SnappersCavity4);
#endif

float _SnappersMaskParams[135];
float3 SnappersMaskParams(const int i, const int j, const int k)
{
	return float3(_SnappersMaskParams[i - 1], _SnappersMaskParams[j - 1], _SnappersMaskParams[k - 1]);
}

float SnappersMaskSum(float3 a, float3 b, float3 c)
{
	float3 sum = a + b + c;
	return sum.x + sum.y + sum.z;
}

#ifdef _SNAPPERS_TEXTURE_ARRAYS
float4 SnappersSample(int index, Texture2DArray array, float2 uv)
{
	return array.Sample(SNAPPERS_SAMPLER, float3(uv, index - 1));
}

float SnappersMaskSum9(int mask123, int mask456, int mask789, float2 uv, const int offset)
{
	return SnappersMaskSum(
		SnappersSample(mask123, _SnappersMask, uv).xyz * SnappersMaskParams(offset + 0, offset + 1, offset + 2),
		SnappersSample(mask456, _SnappersMask, uv).xyz * SnappersMaskParams(offset + 3, offset + 4, offset + 5),
		SnappersSample(mask789, _SnappersMask, uv).xyz * SnappersMaskParams(offset + 6, offset + 7, offset + 8)
	);
}
#endif

void SnappersBlend_float(in float2 uv, in float3 inAlbedo, in float4 inNormal, in float inCavity, out float3 outAlbedo, out float4 outNormal, out float outCavity)
{
	float2 normalUV = uv / 2.0 + float2(0.0, 0.5);
	float2 comp1UV = uv / 2.0 + float2(0.5, 0.5);
	float2 comp2UV = uv / 2.0;
	float2 comp3UV = uv / 2.0 + float2(0.5, 0.0);

#ifdef _SNAPPERS_TEXTURE_ARRAYS
	// mask 1-3
	float AddOp1 = SnappersMaskSum9(1, 2, 3, comp1UV, 1);
	float AddOp2 = SnappersMaskSum9(1, 2, 3, comp2UV, 10);
	float AddOp3 = SnappersMaskSum9(1, 2, 3, comp3UV, 19);

	// mask 4-6
	float AddOp4 = SnappersMaskSum9(4, 5, 6, normalUV, 28);
	float AddOp5 = SnappersMaskSum9(4, 5, 6, comp1UV, 37);
	float AddOp6 = SnappersMaskSum9(4, 5, 6, comp2UV, 46);
	float AddOp7 = SnappersMaskSum9(4, 5, 6, comp3UV, 55);

	// mask 7-9
	float AddOp8 = SnappersMaskSum9(7, 8, 9, normalUV, 64);
	float AddOp9 = SnappersMaskSum9(7, 8, 9, comp1UV, 73);
	float AddOp10 = SnappersMaskSum9(7, 8, 9, comp2UV, 82);
	float AddOp11 = SnappersMaskSum9(7, 8, 9, comp3UV, 91);

	// mask 10-12
	float AddOp12 = SnappersMaskSum9(10, 11, 12, normalUV, 100);
	float AddOp13 = SnappersMaskSum9(10, 11, 12, comp1UV, 109);
	float AddOp14 = SnappersMaskSum9(10, 11, 12, comp2UV, 118);
	float AddOp15 = SnappersMaskSum9(10, 11, 12, comp3UV, 127);
#else
	// mask 1-3
	float AddOp1 = SnappersMaskSum(
		_SnappersMask1.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(1, 2, 3),
		_SnappersMask2.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(4, 5, 6),
		_SnappersMask3.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(7, 8, 9));

	float AddOp2 = SnappersMaskSum(
		_SnappersMask1.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(10, 11, 12),
		_SnappersMask2.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(13, 14, 15),
		_SnappersMask3.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(16, 17, 18));

	float AddOp3 = SnappersMaskSum(
		_SnappersMask1.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(19, 20, 21),
		_SnappersMask2.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(22, 23, 24),
		_SnappersMask3.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(25, 26, 27));

	// mask 4-6
	float AddOp4 = SnappersMaskSum(
		_SnappersMask4.Sample(SNAPPERS_SAMPLER, normalUV).xyz * SnappersMaskParams(28, 29, 30),
		_SnappersMask5.Sample(SNAPPERS_SAMPLER, normalUV).xyz * SnappersMaskParams(31, 32, 33),
		_SnappersMask6.Sample(SNAPPERS_SAMPLER, normalUV).xyz * SnappersMaskParams(34, 35, 36));
	
	float AddOp5 = SnappersMaskSum(
		_SnappersMask4.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(37, 38, 39),
		_SnappersMask5.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(40, 41, 42),
		_SnappersMask6.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(43, 44, 45));

	float AddOp6 = SnappersMaskSum(
		_SnappersMask4.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(46, 47, 48),
		_SnappersMask5.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(49, 50, 51),
		_SnappersMask6.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(52, 53, 54));

	float AddOp7 = SnappersMaskSum(
		_SnappersMask4.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(55, 56, 57),
		_SnappersMask5.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(58, 59, 60),
		_SnappersMask6.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(61, 62, 63));

	// mask 7-9
	float AddOp8 = SnappersMaskSum(
		_SnappersMask7.Sample(SNAPPERS_SAMPLER, normalUV).xyz * SnappersMaskParams(64, 65, 66),
		_SnappersMask8.Sample(SNAPPERS_SAMPLER, normalUV).xyz * SnappersMaskParams(67, 68, 69),
		_SnappersMask9.Sample(SNAPPERS_SAMPLER, normalUV).xyz * SnappersMaskParams(70, 71, 72));

	float AddOp9 = SnappersMaskSum(
		_SnappersMask7.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(73, 74, 75),
		_SnappersMask8.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(76, 77, 78),
		_SnappersMask9.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(79, 80, 81));

	float AddOp10 = SnappersMaskSum(
		_SnappersMask7.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(82, 83, 84),
		_SnappersMask8.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(85, 86, 87),
		_SnappersMask9.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(88, 89, 90));

	float AddOp11 = SnappersMaskSum(
		_SnappersMask7.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(91, 92, 93),
		_SnappersMask8.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(94, 95, 96),
		_SnappersMask9.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(97, 98, 99));

	// mask 10-12
	float AddOp12 = SnappersMaskSum(
		_SnappersMask10.Sample(SNAPPERS_SAMPLER, normalUV).xyz * SnappersMaskParams(100, 101, 102),
		_SnappersMask11.Sample(SNAPPERS_SAMPLER, normalUV).xyz * SnappersMaskParams(103, 104, 105),
		_SnappersMask12.Sample(SNAPPERS_SAMPLER, normalUV).xyz * SnappersMaskParams(106, 107, 108));

	float AddOp13 = SnappersMaskSum(
		_SnappersMask10.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(109, 110, 111),
		_SnappersMask11.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(112, 113, 114),
		_SnappersMask12.Sample(SNAPPERS_SAMPLER, comp1UV).xyz * SnappersMaskParams(115, 116, 117));

	float AddOp14 = SnappersMaskSum(
		_SnappersMask10.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(118, 119, 120),
		_SnappersMask11.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(121, 122, 123),
		_SnappersMask12.Sample(SNAPPERS_SAMPLER, comp2UV).xyz * SnappersMaskParams(124, 125, 126));

	float AddOp15 = SnappersMaskSum(
		_SnappersMask10.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(127, 128, 129),
		_SnappersMask11.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(130, 131, 132),
		_SnappersMask12.Sample(SNAPPERS_SAMPLER, comp3UV).xyz * SnappersMaskParams(133, 134, 135));
#endif

	float AddOpAll = AddOp1 + AddOp2 + AddOp3 + AddOp4 + AddOp5 + AddOp6 + AddOp7 + AddOp8 + AddOp9 + AddOp10 + AddOp11 + AddOp12 + AddOp13 + AddOp14 + AddOp15;
	float AddOpAll_upperbound_1 = min(AddOpAll, 1);

	float IntersectionNorm = AddOpAll - AddOpAll_upperbound_1 + 1.0;
	float IntersectionInv = (1.0 / IntersectionNorm);// * AddOpAll + (1-IntersectionNorm);

	// blend albedo
	{
#ifdef _SNAPPERS_TEXTURE_ARRAYS
		float3 albedoSample1 = SnappersSample(1, _SnappersAlbedo, comp1UV).xyz;
		float3 albedoSample2 = SnappersSample(1, _SnappersAlbedo, comp2UV).xyz;
		float3 albedoSample3 = SnappersSample(1, _SnappersAlbedo, comp3UV).xyz;

		float3 albedoSample4 = SnappersSample(2, _SnappersAlbedo, normalUV).xyz;
		float3 albedoSample5 = SnappersSample(2, _SnappersAlbedo, comp1UV).xyz;
		float3 albedoSample6 = SnappersSample(2, _SnappersAlbedo, comp2UV).xyz;
		float3 albedoSample7 = SnappersSample(2, _SnappersAlbedo, comp3UV).xyz;

		float3 albedoSample8 = SnappersSample(3, _SnappersAlbedo, normalUV).xyz;
		float3 albedoSample9 = SnappersSample(3, _SnappersAlbedo, comp1UV).xyz;
		float3 albedoSample10 = SnappersSample(3, _SnappersAlbedo, comp2UV).xyz;
		float3 albedoSample11 = SnappersSample(3, _SnappersAlbedo, comp3UV).xyz;

		float3 albedoSample12 = SnappersSample(4, _SnappersAlbedo, normalUV).xyz;
		float3 albedoSample13 = SnappersSample(4, _SnappersAlbedo, comp1UV).xyz;
		float3 albedoSample14 = SnappersSample(4, _SnappersAlbedo, comp2UV).xyz;
		float3 albedoSample15 = SnappersSample(4, _SnappersAlbedo, comp3UV).xyz;
#else
		float3 albedoSample1 = _SnappersAlbedo1.Sample(SNAPPERS_SAMPLER, comp1UV).xyz;
		float3 albedoSample2 = _SnappersAlbedo1.Sample(SNAPPERS_SAMPLER, comp2UV).xyz;
		float3 albedoSample3 = _SnappersAlbedo1.Sample(SNAPPERS_SAMPLER, comp3UV).xyz;

		float3 albedoSample4 = _SnappersAlbedo2.Sample(SNAPPERS_SAMPLER, normalUV).xyz;
		float3 albedoSample5 = _SnappersAlbedo2.Sample(SNAPPERS_SAMPLER, comp1UV).xyz;
		float3 albedoSample6 = _SnappersAlbedo2.Sample(SNAPPERS_SAMPLER, comp2UV).xyz;
		float3 albedoSample7 = _SnappersAlbedo2.Sample(SNAPPERS_SAMPLER, comp3UV).xyz;

		float3 albedoSample8 = _SnappersAlbedo3.Sample(SNAPPERS_SAMPLER, normalUV).xyz;
		float3 albedoSample9 = _SnappersAlbedo3.Sample(SNAPPERS_SAMPLER, comp1UV).xyz;
		float3 albedoSample10 = _SnappersAlbedo3.Sample(SNAPPERS_SAMPLER, comp2UV).xyz;
		float3 albedoSample11 = _SnappersAlbedo3.Sample(SNAPPERS_SAMPLER, comp3UV).xyz;

		float3 albedoSample12 = _SnappersAlbedo4.Sample(SNAPPERS_SAMPLER, normalUV).xyz;
		float3 albedoSample13 = _SnappersAlbedo4.Sample(SNAPPERS_SAMPLER, comp1UV).xyz;
		float3 albedoSample14 = _SnappersAlbedo4.Sample(SNAPPERS_SAMPLER, comp2UV).xyz;
		float3 albedoSample15 = _SnappersAlbedo4.Sample(SNAPPERS_SAMPLER, comp3UV).xyz;
#endif

		outAlbedo = inAlbedo * (1 - AddOpAll_upperbound_1) + IntersectionInv *
		(
			albedoSample1 * AddOp1 +
			albedoSample2 * AddOp2 +
			albedoSample3 * AddOp3 +
			albedoSample4 * AddOp4 +
			albedoSample5 * AddOp5 +
			albedoSample6 * AddOp6 +
			albedoSample7 * AddOp7 +
			albedoSample8 * AddOp8 +
			albedoSample9 * AddOp9 +
			albedoSample10 * AddOp10 +
			albedoSample11 * AddOp11 +
			albedoSample12 * AddOp12 +
			albedoSample13 * AddOp13 +
			albedoSample14 * AddOp14 +
			albedoSample15 * AddOp15
		);
	}

	// blend normal
	{
#ifdef _SNAPPERS_TEXTURE_ARRAYS
		float4 normalSample1 = SnappersSample(1, _SnappersNormal, comp1UV);
		float4 normalSample2 = SnappersSample(1, _SnappersNormal, comp2UV);
		float4 normalSample3 = SnappersSample(1, _SnappersNormal, comp3UV);

		float4 normalSample4 = SnappersSample(2, _SnappersNormal, normalUV);
		float4 normalSample5 = SnappersSample(2, _SnappersNormal, comp1UV);
		float4 normalSample6 = SnappersSample(2, _SnappersNormal, comp2UV);
		float4 normalSample7 = SnappersSample(2, _SnappersNormal, comp3UV);

		float4 normalSample8 = SnappersSample(3, _SnappersNormal, normalUV);
		float4 normalSample9 = SnappersSample(3, _SnappersNormal, comp1UV);
		float4 normalSample10 = SnappersSample(3, _SnappersNormal, comp2UV);
		float4 normalSample11 = SnappersSample(3, _SnappersNormal, comp3UV);

		float4 normalSample12 = SnappersSample(4, _SnappersNormal, normalUV);
		float4 normalSample13 = SnappersSample(4, _SnappersNormal, comp1UV);
		float4 normalSample14 = SnappersSample(4, _SnappersNormal, comp2UV);
		float4 normalSample15 = SnappersSample(4, _SnappersNormal, comp3UV);
#else
		//float4 normalBase = _NormalMap.Sample(sampler_NormalMap, uv);
		float4 normalSample1 = _SnappersNormal1.Sample(SNAPPERS_SAMPLER, comp1UV);
		float4 normalSample2 = _SnappersNormal1.Sample(SNAPPERS_SAMPLER, comp2UV);
		float4 normalSample3 = _SnappersNormal1.Sample(SNAPPERS_SAMPLER, comp3UV);

		float4 normalSample4 = _SnappersNormal2.Sample(SNAPPERS_SAMPLER, normalUV);
		float4 normalSample5 = _SnappersNormal2.Sample(SNAPPERS_SAMPLER, comp1UV);
		float4 normalSample6 = _SnappersNormal2.Sample(SNAPPERS_SAMPLER, comp2UV);
		float4 normalSample7 = _SnappersNormal2.Sample(SNAPPERS_SAMPLER, comp3UV);

		float4 normalSample8 = _SnappersNormal3.Sample(SNAPPERS_SAMPLER, normalUV);
		float4 normalSample9 = _SnappersNormal3.Sample(SNAPPERS_SAMPLER, comp1UV);
		float4 normalSample10 = _SnappersNormal3.Sample(SNAPPERS_SAMPLER, comp2UV);
		float4 normalSample11 = _SnappersNormal3.Sample(SNAPPERS_SAMPLER, comp3UV);

		float4 normalSample12 = _SnappersNormal4.Sample(SNAPPERS_SAMPLER, normalUV);
		float4 normalSample13 = _SnappersNormal4.Sample(SNAPPERS_SAMPLER, comp1UV);
		float4 normalSample14 = _SnappersNormal4.Sample(SNAPPERS_SAMPLER, comp2UV);
		float4 normalSample15 = _SnappersNormal4.Sample(SNAPPERS_SAMPLER, comp3UV);
#endif

		outNormal = inNormal * (1 - AddOpAll_upperbound_1) + IntersectionInv *
		(
			normalSample1 * AddOp1 +
			normalSample2 * AddOp2 +
			normalSample3 * AddOp3 +
			normalSample4 * AddOp4 +
			normalSample5 * AddOp5 +
			normalSample6 * AddOp6 +
			normalSample7 * AddOp7 +
			normalSample8 * AddOp8 +
			normalSample9 * AddOp9 +
			normalSample10 * AddOp10 +
			normalSample11 * AddOp11 +
			normalSample12 * AddOp12 +
			normalSample13 * AddOp13 +
			normalSample14 * AddOp14 +
			normalSample15 * AddOp15
		);
	}

	// blend cavity
	{
#ifdef _SNAPPERS_TEXTURE_ARRAYS
		float cavitySample1 = SnappersSample(1, _SnappersCavity, comp1UV).r;
		float cavitySample2 = SnappersSample(1, _SnappersCavity, comp2UV).r;
		float cavitySample3 = SnappersSample(1, _SnappersCavity, comp3UV).r;

		float cavitySample4 = SnappersSample(2, _SnappersCavity, normalUV).r;
		float cavitySample5 = SnappersSample(2, _SnappersCavity, comp1UV).r;
		float cavitySample6 = SnappersSample(2, _SnappersCavity, comp2UV).r;
		float cavitySample7 = SnappersSample(2, _SnappersCavity, comp3UV).r;

		float cavitySample8 = SnappersSample(3, _SnappersCavity, normalUV).r;
		float cavitySample9 = SnappersSample(3, _SnappersCavity, comp1UV).r;
		float cavitySample10 = SnappersSample(3, _SnappersCavity, comp2UV).r;
		float cavitySample11 = SnappersSample(3, _SnappersCavity, comp3UV).r;

		float cavitySample12 = SnappersSample(4, _SnappersCavity, normalUV).r;
		float cavitySample13 = SnappersSample(4, _SnappersCavity, comp1UV).r;
		float cavitySample14 = SnappersSample(4, _SnappersCavity, comp2UV).r;
		float cavitySample15 = SnappersSample(4, _SnappersCavity, comp3UV).r;
#else
		float cavitySample1 = _SnappersCavity1.Sample(SNAPPERS_SAMPLER, comp1UV).r;
		float cavitySample2 = _SnappersCavity1.Sample(SNAPPERS_SAMPLER, comp2UV).r;
		float cavitySample3 = _SnappersCavity1.Sample(SNAPPERS_SAMPLER, comp3UV).r;

		float cavitySample4 = _SnappersCavity2.Sample(SNAPPERS_SAMPLER, normalUV).r;
		float cavitySample5 = _SnappersCavity2.Sample(SNAPPERS_SAMPLER, comp1UV).r;
		float cavitySample6 = _SnappersCavity2.Sample(SNAPPERS_SAMPLER, comp2UV).r;
		float cavitySample7 = _SnappersCavity2.Sample(SNAPPERS_SAMPLER, comp3UV).r;

		float cavitySample8 = _SnappersCavity3.Sample(SNAPPERS_SAMPLER, normalUV).r;
		float cavitySample9 = _SnappersCavity3.Sample(SNAPPERS_SAMPLER, comp1UV).r;
		float cavitySample10 = _SnappersCavity3.Sample(SNAPPERS_SAMPLER, comp2UV).r;
		float cavitySample11 = _SnappersCavity3.Sample(SNAPPERS_SAMPLER, comp3UV).r;

		float cavitySample12 = _SnappersCavity4.Sample(SNAPPERS_SAMPLER, normalUV).r;
		float cavitySample13 = _SnappersCavity4.Sample(SNAPPERS_SAMPLER, comp1UV).r;
		float cavitySample14 = _SnappersCavity4.Sample(SNAPPERS_SAMPLER, comp2UV).r;
		float cavitySample15 = _SnappersCavity4.Sample(SNAPPERS_SAMPLER, comp3UV).r;
#endif

		outCavity = inCavity * (1 - AddOpAll_upperbound_1) + IntersectionInv *
		(
			cavitySample1 * AddOp1 +
			cavitySample2 * AddOp2 +
			cavitySample3 * AddOp3 +
			cavitySample4 * AddOp4 +
			cavitySample5 * AddOp5 +
			cavitySample6 * AddOp6 +
			cavitySample7 * AddOp7 +
			cavitySample8 * AddOp8 +
			cavitySample9 * AddOp9 +
			cavitySample10 * AddOp10 +
			cavitySample11 * AddOp11 +
			cavitySample12 * AddOp12 +
			cavitySample13 * AddOp13 +
			cavitySample14 * AddOp14 +
			cavitySample15 * AddOp15
		);
	}
}

#endif//__SNAPPERSBLEND_HLSL__
