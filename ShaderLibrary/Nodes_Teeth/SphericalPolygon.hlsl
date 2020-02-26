#ifndef __SPHERICALPOLYGON_HLSL__
#define __SPHERICALPOLYGON_HLSL__

#ifndef SPHERICALPOLYGON_MAX_VERTS
#define SPHERICALPOLYGON_MAX_VERTS 3
#endif
#ifndef SPHERICALPOLYGON_NUM_VERTS
#define SPHERICALPOLYGON_NUM_VERTS 3
#endif

void SphericalPolygon_CalcInteriorAngles(in float3 P[SPHERICALPOLYGON_MAX_VERTS], out float A[SPHERICALPOLYGON_MAX_VERTS])
{
	const int LAST_VERT = (SPHERICALPOLYGON_NUM_VERTS - 1);

	float3 N[SPHERICALPOLYGON_MAX_VERTS];

	// calc plane normals
	// where N[i] = normal of incident plane
	//   eg. N[i+0] = cross(C, A);
	//       N[i+1] = cross(A, B);
	{
		N[0] = -normalize(cross(P[LAST_VERT], P[0]));
		for (int i = 1; i != SPHERICALPOLYGON_NUM_VERTS; i++)
		{
			N[i] = -normalize(cross(P[i - 1], P[i]));
		}
	}

	// calc interior angles
	{
		for (int i = 0; i != LAST_VERT; i++)
		{
			A[i] = PI - sign(dot(N[i], P[i + 1])) * acos(clamp(dot(N[i], N[i + 1]), -1.0, 1.0));
		}
		A[LAST_VERT] = PI - sign(dot(N[LAST_VERT], P[0])) * acos(clamp(dot(N[LAST_VERT], N[0]), -1.0, 1.0));
	}

	/*
	float3 nCA = cross(C, A);// plane normals
	float3 nAB = cross(A, B);
	float3 nBC = cross(B, C);

	float wA = PI - sign(dot(nCA, B)) * acos(dot(nCA, nAB));
	float wB = PI - sign(dot(nAB, C)) * acos(dot(nAB, nBC));
	float wC = PI - sign(dot(nBC, A)) * acos(dot(nBC, nCA));

	return float3(wA, wB, wC);
	*/
}

void SphericalPolygon_CalcAreaFromInteriorAngles(in float A[SPHERICALPOLYGON_MAX_VERTS], out float area)
{
	float E = 0.0;
	for (int i = 0; i != SPHERICALPOLYGON_NUM_VERTS; i++)
	{
		E += A[i];
	}
	area = E - (SPHERICALPOLYGON_NUM_VERTS - 2.0) * PI;
}

void SphericalPolygon_CalcAreaFromProjectedPositions(in float3 P[SPHERICALPOLYGON_MAX_VERTS], out float area)
{
	float A[SPHERICALPOLYGON_MAX_VERTS];
	SphericalPolygon_CalcInteriorAngles(P, A);
	SphericalPolygon_CalcAreaFromInteriorAngles(A, area);
}

#endif//__SPHERICALPOLYGON_HLSL__
