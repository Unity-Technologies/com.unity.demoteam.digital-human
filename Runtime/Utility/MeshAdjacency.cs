using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public class MeshAdjacency
	{
		public LinkedIndexListArray vertexTriangles;
		public LinkedIndexListArray vertexVertices;
		public LinkedIndexListArray vertexWelded;
		public int[] vertexResolve;
		public int vertexCount;

		public LinkedIndexListArray triangleTriangles;
		public LinkedIndexListArray triangleVertices;
		public int triangleCount;

		public MeshAdjacency(MeshBuffers meshBuffers, bool welded = false)
		{
			LoadFrom(meshBuffers, welded);
		}

		public void LoadFrom(MeshBuffers meshBuffers, bool welded = false)
		{
			vertexCount = meshBuffers.vertexCount;
			vertexTriangles.Allocate(vertexCount, vertexCount * 8);
			vertexVertices.Allocate(vertexCount, vertexCount * 8);
			vertexWelded.Allocate(vertexCount, vertexCount);

			ArrayUtils.ResizeCheckedIfLessThan(ref vertexResolve, vertexCount);

			triangleCount = meshBuffers.triangleCount / 3;
			triangleTriangles.Allocate(triangleCount, triangleCount * 8);
			triangleVertices.Allocate(triangleCount, triangleCount * 3);

			int[] triangles = meshBuffers.triangles;

			// build vertex-triangle
			{
				for (int i = 0; i != triangleCount; i++)
				{
					int _0 = i * 3;
					int v0 = triangles[_0 + 0];
					int v1 = triangles[_0 + 1];
					int v2 = triangles[_0 + 2];

					vertexTriangles.Append(v0, i);
					vertexTriangles.Append(v1, i);
					vertexTriangles.Append(v2, i);
				}
			}

			// build vertex-welded
			unsafe
			{
				if (welded)
				{
					// for each vertex
					//   if vertex != closest vertex
					//     replace references to vertex with closest vertex
					triangles = triangles.Clone() as int[];

					using (var vertexWeldedMap = new UnsafeArrayBool(meshBuffers.vertexCount))
					{
						vertexWeldedMap.Clear(false);

						var vertexBSP = new KdTree3(meshBuffers.vertexPositions, meshBuffers.vertexCount);

						for (int i = 0; i != vertexCount; i++)
						{
							int j = vertexBSP.FindNearest(ref meshBuffers.vertexPositions[i]);
							if (j != i)
							{
								//Debug.Assert(vertexWeldedMap.val[j] == false);

								// replace references to i with j, keeping j
								foreach (var triangle in vertexTriangles[i])
								{
									int _0 = triangle * 3;
									int v0 = triangles[_0 + 0];
									int v1 = triangles[_0 + 1];
									//..v2 = triangles[_0 + 2];

									if (v0 == i)
										triangles[_0 + 0] = j;
									else if (v1 == i)
										triangles[_0 + 1] = j;
									else // (v2 == i)
										triangles[_0 + 2] = j;

									// store i under j, so we can recover i at a later time
									if (vertexWeldedMap.val[i] == false)
									{
										vertexWeldedMap.val[i] = true;
										vertexWelded.Append(j, i);
									}
								}
							}
						}
					}

					// rebuild vertex-triangle
					vertexTriangles.Clear();

					for (int i = 0; i != triangleCount; i++)
					{
						int _0 = i * 3;
						int v0 = triangles[_0 + 0];
						int v1 = triangles[_0 + 1];
						int v2 = triangles[_0 + 2];

						vertexTriangles.Append(v0, i);
						vertexTriangles.Append(v1, i);
						vertexTriangles.Append(v2, i);
					}
				}
			}

			// build vertex-resolve
			{
				for (int i = 0; i != vertexCount; i++)
				{
					vertexResolve[i] = i;
				}

				for (int i = 0; i != vertexCount; i++)
				{
					foreach (var j in vertexWelded[i])
					{
						vertexResolve[j] = i;
					}
				}
			}

			// build vertex-vertex
			unsafe
			{
				using (var vertexAdded = new UnsafeArrayBool(vertexCount))
				{
					vertexAdded.Clear(false);

					for (int i = 0; i != vertexCount; i++)
					{
						foreach (int triangle in vertexTriangles[i])
						{
							int _0 = triangle * 3;
							int v0 = triangles[_0 + 0];
							int v1 = triangles[_0 + 1];
							int v2 = triangles[_0 + 2];

							int vA, vB;
							if (i == v0)
							{
								vA = v1;
								vB = v2;
							}
							else if (i == v1)
							{
								vA = v2;
								vB = v0;
							}
							else // (i == v2)
							{
								vA = v0;
								vB = v1;
							}

							if (vertexAdded.val[vA] == false && (vertexAdded.val[vA] = true))
								vertexVertices.Append(i, vA);

							if (vertexAdded.val[vB] == false && (vertexAdded.val[vB] = true))
								vertexVertices.Append(i, vB);
						}

						foreach (int j in vertexVertices[i])
							vertexAdded.val[j] = false;
					}
				}
			}

			// build triangle-triangle
			unsafe
			{
				using (var triangleAdded = new UnsafeArrayBool(triangleCount))
				{
					triangleAdded.Clear(false);

					for (int i = 0; i != triangleCount; i++)
					{
						int _0 = i * 3;
						int v0 = triangles[_0 + 0];
						int v1 = triangles[_0 + 1];
						int v2 = triangles[_0 + 2];

						triangleAdded.val[i] = true;

						foreach (int j in vertexTriangles[v0])
							if (triangleAdded.val[j] == false && (triangleAdded.val[j] = true))
								triangleTriangles.Append(i, j);

						foreach (int j in vertexTriangles[v1])
							if (triangleAdded.val[j] == false && (triangleAdded.val[j] = true))
								triangleTriangles.Append(i, j);

						foreach (int j in vertexTriangles[v2])
							if (triangleAdded.val[j] == false && (triangleAdded.val[j] = true))
								triangleTriangles.Append(i, j);

						triangleAdded.val[i] = false;

						foreach (int j in triangleTriangles[i])
							triangleAdded.val[j] = false;
					}
				}
			}

			// build triangle-vertex
			{
				for (int i = 0; i != triangleCount; i++)
				{
					int _0 = i * 3;
					int v0 = triangles[_0 + 0];
					int v1 = triangles[_0 + 1];
					int v2 = triangles[_0 + 2];

					triangleVertices.Append(i, v0);
					triangleVertices.Append(i, v1);
					triangleVertices.Append(i, v2);
				}
			}
		}
	}
}
