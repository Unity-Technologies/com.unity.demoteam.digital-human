#ifndef __TEETHATTENUATION_HLSL__
#define __TEETHATTENUATION_HLSL__

#if !defined(TEETH_DATA_FIXED_6) && !defined(TEETH_DATA_VARIABLE_32)
#define TEETH_DATA_FIXED_6
#endif
#if !defined(TEETH_ATTN_NONE) && !defined(TEETH_ATTN_LINEAR) && !defined(TEETH_ATTN_SKYPOLYGON)
#define TEETH_ATTN_NONE
#endif

#if defined(TEETH_DATA_FIXED_6)
	#define SPHERICALPOLYGON_MAX_VERTS 6
	#define SPHERICALPOLYGON_NUM_VERTS 6
#elif defined(TEETH_DATA_VARIABLE_32)
	#define SPHERICALPOLYGON_MAX_VERTS 32
	#define SPHERICALPOLYGON_NUM_VERTS _TeethVertexCount
#endif

uniform float4 _TeethParams;// x = lit potential min, y = lit potential max
uniform ByteAddressBuffer _TeethVertexData;
uniform Buffer<int> _TeethVertexDataIndices;
uniform int _TeethVertexDataStride;
uniform int _TeethVertexCount;
uniform int _UseTeethVertexDataIndirection;

#include "SphericalPolygon.hlsl"

float3 GetTeethVertex(int index)
{
	if(_UseTeethVertexDataIndirection != 0)
	{
		index = _TeethVertexDataIndices[index];

	} 
	return asfloat(_TeethVertexData.Load3(_TeethVertexDataStride * index));
	
	
}

void TeethAttenuation_float(in float3 positionWS, out float attnPure, out float attnBiased)
{
	attnPure = 1.0;
	attnBiased = 1.0;
	{
#if defined(TEETH_ATTN_NONE)

		return;// no modification

#elif defined(TEETH_ATTN_LINEAR)

		float3 posFront = GetTeethVertex(0).xyz;
		float3 posBack = GetTeethVertex(1).xyz;

		float3 v = posFront - posBack;
		float dd = dot(v, v);

		attnPure = saturate(dot(v / dd, positionWS - posBack));
		attnBiased = _TeethParams.x + attnPure * (_TeethParams.y - _TeethParams.x);

#elif defined(TEETH_ATTN_SKYPOLYGON)

		float3 P[SPHERICALPOLYGON_MAX_VERTS];
		for (int i = 0; i < SPHERICALPOLYGON_NUM_VERTS; i++)
		{
			P[i] = normalize(GetTeethVertex(i).xyz - positionWS);
		}

		float incidentMax = 2.0 * PI;// hemisphere
		float incidentSky;
		SphericalPolygon_CalcAreaFromProjectedPositions(P, incidentSky);

		attnPure = saturate(incidentSky / incidentMax);
		attnPure = saturate(1.0 - pow(1.0 - attnPure, _TeethParams.z));

		attnBiased = _TeethParams.x + attnPure * (_TeethParams.y - _TeethParams.x);

#endif
	}
}

#endif//__TEETHATTENUATION_HLSL__