#ifndef __F_SCHLICK_H__
#define __F_SCHLICK_H__

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"

void F_Schlick_float(in float f0, in float f90, in float u, out float result)
{
	/* BSDF.hlsl:

	real x = 1.0 - u;
	real x2 = x * x;
	real x5 = x * x2 * x2;
	return (f90 - f0) * x5 + f0;                // sub mul mul mul sub mad

	*/
	result = F_Schlick(f0, f90, u);// f0 when viewed straight on, f90 at grazing angle
}

#endif//__F_SCHLICK_H__
