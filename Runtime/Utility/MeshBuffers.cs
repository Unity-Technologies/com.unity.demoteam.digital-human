using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public class MeshBuffers
	{
		public static List<Vector3> __tempVertexPositions = new List<Vector3>();
		public static List<Vector4> __tempVertexTangents = new List<Vector4>();
		public static List<Vector3> __tempVertexNormals = new List<Vector3>();

		public static List<int> __tempIndices = new List<int>();

		public int vertexCount;
		public Vector3[] vertexPositions;
		public Vector4[] vertexTangents;
		public Vector3[] vertexNormals;

		public int triangleCount;
		public int[] triangles;

		public MeshBuffers(int vertexCapacity)
		{
			vertexCount = 0;
			vertexPositions = new Vector3[vertexCapacity];
			vertexTangents = new Vector4[vertexCapacity];
			vertexNormals = new Vector3[vertexCapacity];

			triangleCount = 0;
			triangles = new int[vertexCapacity];
		}

		public MeshBuffers(Mesh mesh) : this(mesh.vertexCount)
		{
			LoadFrom(mesh);
		}

		public void LoadFrom(Mesh mesh)
		{
			// copy vertices
			Profiler.BeginSample("copy-verts");
			{
				mesh.GetVertices(__tempVertexPositions);
				mesh.GetTangents(__tempVertexTangents);
				mesh.GetNormals(__tempVertexNormals);

				vertexCount = mesh.vertexCount;

				ArrayUtils.ResizeCheckedIfLessThan(ref vertexPositions, vertexCount);
				__tempVertexPositions.CopyTo(vertexPositions);

				ArrayUtils.ResizeCheckedIfLessThan(ref vertexTangents, vertexCount);
				__tempVertexTangents.CopyTo(vertexTangents);

				ArrayUtils.ResizeCheckedIfLessThan(ref vertexNormals, vertexCount);
				__tempVertexNormals.CopyTo(vertexNormals);
			}
			Profiler.EndSample();

			// copy triangles
			Profiler.BeginSample("copy-tris");
			{
				triangleCount = 0;

				int submeshCount = mesh.subMeshCount;
				if (submeshCount > 0)
				{
					for (int i = 0; i != submeshCount; i++)
					{
						var topology = mesh.GetTopology(i);
						if (topology != MeshTopology.Triangles)
							continue;

						mesh.GetTriangles(__tempIndices, i);

						int submeshTriangleCount = __tempIndices.Count;
						if (submeshTriangleCount > 0)
						{
							ArrayUtils.ResizeCheckedIfLessThan(ref triangles, triangleCount + submeshTriangleCount);
							__tempIndices.CopyTo(triangles, triangleCount);
						}

						triangleCount += submeshTriangleCount;
					}
				}
			}
			Profiler.EndSample();
		}

		public void LoadFrom(in NativeMeshSOA nativeMesh)
		{
			// copy vertices
			Profiler.BeginSample("copy-verts");
			{
				vertexCount = nativeMesh.vertexCount;

				ArrayUtils.ResizeCheckedIfLessThan(ref vertexPositions, vertexCount);
				ArrayUtils.ResizeCheckedIfLessThan(ref vertexTangents, vertexCount);
				ArrayUtils.ResizeCheckedIfLessThan(ref vertexNormals, vertexCount);

				nativeMesh.vertexPositions.CopyTo(vertexPositions);
				Array.Clear(vertexTangents, 0, vertexTangents.Length);
				nativeMesh.vertexNormals.CopyTo(vertexNormals);
			}
			Profiler.EndSample();

			// copy triangles
			Profiler.BeginSample("copy-tris");
			{
				triangleCount = nativeMesh.faceIndicesCount;

				ArrayUtils.ResizeCheckedIfLessThan(ref triangles, triangleCount);

				nativeMesh.faceIndices.CopyTo(triangles);
			}
			Profiler.EndSample();
		}

		public void LoadPositionsFrom(Mesh mesh)
		{
			Profiler.BeginSample("inject-verts-pos");
			{
				mesh.GetVertices(__tempVertexPositions);

				ArrayUtils.ResizeCheckedIfLessThan(ref vertexPositions, vertexCount);
				__tempVertexPositions.CopyTo(vertexPositions);
			}
			Profiler.EndSample();
		}

		public void LoadNormalsFrom(Mesh mesh)
		{
			Profiler.BeginSample("inject-verts-nrm");
			{
				mesh.GetNormals(__tempVertexNormals);

				ArrayUtils.ResizeCheckedIfLessThan(ref vertexNormals, vertexCount);
				__tempVertexNormals.CopyTo(vertexNormals);
			}
			Profiler.EndSample();
		}

		public void CopyTo(MeshBuffers meshBuffers)
		{
			Profiler.BeginSample("copy-existing");
			{
				ArrayUtils.CopyChecked(vertexPositions, ref meshBuffers.vertexPositions, vertexCount);
				ArrayUtils.CopyChecked(vertexTangents, ref meshBuffers.vertexTangents, vertexCount);
				ArrayUtils.CopyChecked(vertexNormals, ref meshBuffers.vertexNormals, vertexCount);
				meshBuffers.vertexCount = vertexCount;

				ArrayUtils.CopyChecked(triangles, ref meshBuffers.triangles, triangleCount);
				meshBuffers.triangleCount = triangleCount;
			}
			Profiler.EndSample();
		}

		public void ApplyRotation(Quaternion q)
		{
			for (int i = 0; i != vertexCount; i++)
			{
				vertexPositions[i] = q * vertexPositions[i];
			}
			for (int i = 0; i != vertexCount; i++)
			{
				Vector3 tangent3 = q * ((Vector3)vertexTangents[i]);
				vertexTangents[i].x = tangent3.x;
				vertexTangents[i].y = tangent3.y;
				vertexTangents[i].z = tangent3.z;
				vertexTangents[i].w = vertexTangents[i].w;
			}
			for (int i = 0; i != vertexCount; i++)
			{
				vertexNormals[i] = q * vertexNormals[i];
			}
		}

		public void ApplyScale(float s)
		{
			for (int i = 0; i != vertexCount; i++)
			{
				vertexPositions[i] = s * vertexPositions[i];
			}
		}

		public void ApplySmoothing(MeshAdjacency meshAdjacency, int iterations)
		{
			Debug.Assert(vertexCount == meshAdjacency.vertexCount);
			unsafe
			{
				fixed (Vector3* __vertexPositions = vertexPositions)
				{
					using (var v = new UnsafeArrayVector3(meshAdjacency.vertexCount))
					{
						while (iterations-- >= 0)
						{
							for (int i = 0; i != meshAdjacency.vertexCount; i++)
							{
								var s = new Vector3(0.0f, 0.0f, 0.0f);
								var d = meshAdjacency.vertexVertices.lists[i].size;
								foreach (int j in meshAdjacency.vertexVertices[i])
								{
									s.x += __vertexPositions[j].x;
									s.y += __vertexPositions[j].y;
									s.z += __vertexPositions[j].z;
								}
								v.val[i] = s / d;
							}
							UnsafeUtility.MemCpy(__vertexPositions, v.val, sizeof(Vector3) * vertexCount);
						}
					}
				}
			}
		}

		public void ApplyWeldedChanges(MeshAdjacency meshAdjacency)
		{
			Debug.Assert(vertexCount == meshAdjacency.vertexCount);
			for (int i = 0; i != meshAdjacency.vertexCount; i++)
			{
				foreach (int j in meshAdjacency.vertexWelded[i])
				{
					vertexPositions[j] = vertexPositions[i];
					vertexTangents[j] = vertexTangents[i];
					vertexNormals[j] = vertexNormals[i];
				}
			}
		}

		public Vector3 CalcMeshCenter()
		{
			Vector3 average = Vector3.zero;
			for (int i = 0; i != vertexCount; i++)
			{
				average += vertexPositions[i];
			}
			return average / vertexCount;
		}

		public Vector3 CalcAABBCenter()
		{
			Vector3 min;
			Vector3 max;
			CalcAABBMinMax(out min, out max);
			return 0.5f * (min + max);
		}

		public Vector3 CalcAABBExtent()
		{
			Vector3 min;
			Vector3 max;
			CalcAABBMinMax(out min, out max);
			return 0.5f * (max - min);
		}

		public void CalcAABBMinMax(out Vector3 min, out Vector3 max)
		{
			min = Vector3.positiveInfinity;
			max = Vector3.negativeInfinity;
			for (int i = 0; i != vertexCount; i++)
			{
				min = Vector3.Min(min, vertexPositions[i]);
				max = Vector3.Max(max, vertexPositions[i]);
			}
		}

		public void RecalculateNormals(MeshAdjacency meshAdjacency)
		{
			unsafe
			{
				using (var triangleProducts = new UnsafeArrayVector3(meshAdjacency.triangleCount))
				{
					triangleProducts.Clear(Vector3.zero);

					for (int i = 0; i != meshAdjacency.triangleCount; i++)
					{
						var _e = meshAdjacency.triangleVertices[i].GetEnumerator();
						int v0 = _e.ReadNext();
						int v1 = _e.ReadNext();
						int v2 = _e.ReadNext();

						Vector3 v0v1 = vertexPositions[v1] - vertexPositions[v0];
						Vector3 v0v2 = vertexPositions[v2] - vertexPositions[v0];

						triangleProducts.val[i] = Vector3.Cross(v0v1, v0v2);
					}

					for (int i = 0; i != meshAdjacency.vertexCount; i++)
					{
						Vector3 sumProducts = Vector3.zero;
						foreach (var triangle in meshAdjacency.vertexTriangles[i])
						{
							sumProducts += triangleProducts.val[triangle];
						}

						var sumProductsSqNorm = Vector3.SqrMagnitude(sumProducts);
						if (sumProductsSqNorm != 0.0f)
						{
							vertexNormals[i] = sumProducts / Mathf.Sqrt(sumProductsSqNorm);
						}
						else
						{
							var numVertexTriangles = meshAdjacency.vertexTriangles.lists[i].size;
							if (numVertexTriangles != 0)
							{
								Debug.LogError("degenerate vertex " + i + ": all " + numVertexTriangles + " adjacent triangles have zero area");
								break;
							}
						}
					}
				}
			}
		}
	}
}
