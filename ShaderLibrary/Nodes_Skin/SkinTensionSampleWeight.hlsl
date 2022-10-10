#ifndef __SKINTENSIONSAMPLEWEIGHT_HLSL__
#define __SKINTENSIONSAMPLEWEIGHT_HLSL__

int _SkinTensionDataValid;
StructuredBuffer<float> _TensionWeightsBuffer;

void SkinTensionSampleWeight_float(in uint vertexID, out float weight)
{
#ifdef _USE_TENSION_MAPS
	if(_SkinTensionDataValid == 1)
	{
		weight = _TensionWeightsBuffer[vertexID];
	} 
	else
#endif
	{
		weight = 0.f;
	}
}

#endif//__SKINTENSIONSAMPLEWEIGHT_HLSL__
