using System.Collections.Generic;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public class MeshEdges
	{
		public Edge[] edges;
		public struct Edge
		{
			public int p1;
			public int p2;
		}

		private static HashSet<ulong> __hashedPairs = new HashSet<ulong>();
		private static void __hashedPairsClear() { __hashedPairs.Clear(); }
		private static bool __hashedPairsAdd(int i, int j)
		{
			ulong i32 = (ulong)i;
			ulong j32 = (ulong)j;

			ulong hashedPair = (i < j) ? (i32 | (j32 << 32)) : (j32 | (i32 << 32));
			if (__hashedPairs.Contains(hashedPair) == false)
			{
				__hashedPairs.Add(hashedPair);
				return true;
			}
			else
			{
				return false;
			}
		}

		public MeshEdges()
		{
			ArrayUtils.ResizeChecked(ref edges, 0);
		}

		public MeshEdges(int[] triangles)
		{
			LoadFrom(triangles);
		}

		public void LoadFrom(int[] triangles)
		{
			unsafe
			{
				var numTriangles = triangles.Length / 3;
				var numEdges = 0;

				var buffer = (Edge*)UnsafeUtility.Malloc(sizeof(Edge) * numTriangles * 3, 1, Unity.Collections.Allocator.Temp);

				__hashedPairsClear();

				for (int triangleIndex = 0; triangleIndex != numTriangles; triangleIndex++)
				{
					var i = triangles[triangleIndex * 3 + 0];
					var j = triangles[triangleIndex * 3 + 1];
					var k = triangles[triangleIndex * 3 + 2];

					if (__hashedPairsAdd(i, j))
						buffer[numEdges++] = new Edge { p1 = i, p2 = j };

					if (__hashedPairsAdd(j, k))
						buffer[numEdges++] = new Edge { p1 = j, p2 = k };

					if (__hashedPairsAdd(k, i))
						buffer[numEdges++] = new Edge { p1 = k, p2 = i };
				}

				ArrayUtils.ResizeChecked(ref edges, numEdges);

				fixed (Edge* edgesPtr = edges)
				{
					UnsafeUtility.MemCpy(edgesPtr, buffer, sizeof(Edge) * numEdges);
					UnsafeUtility.Free(buffer, Unity.Collections.Allocator.Temp);
				}
				//Debug.Log("numTriangles = " + numTriangles + ", numEdges = " + numEdges);
			}
		}

		public void ComputeLengths(ref float[] lengths, Vector3[] positions)
		{
			ArrayUtils.ResizeChecked(ref lengths, edges.Length);
			for (int i = 0; i != edges.Length; i++)
			{
				Vector3 p1 = positions[edges[i].p1];
				Vector3 p2 = positions[edges[i].p2];
				lengths[i] = Vector3.Magnitude(p2 - p1);
			}
		}

		public void ComputeCurvatures(ref float[] curvatures, Vector3[] positions, Vector3[] normals)
		{
			ArrayUtils.ResizeChecked(ref curvatures, edges.Length);
			for (int i = 0; i != edges.Length; i++)
			{
				// https://computergraphics.stackexchange.com/a/1719
				var p1 = positions[edges[i].p1];
				var p2 = positions[edges[i].p2];
				var p1p2 = p2 - p1;
				var squaredLength = Vector3.Dot(p1p2, p1p2);
				if (squaredLength != 0.0f)
				{
					var n1 = normals[edges[i].p1];
					var n2 = normals[edges[i].p2];
					curvatures[i] = Vector3.Dot(n2 - n1, p1p2) / squaredLength;
				}
				else
				{
					curvatures[i] = 0.0f;
				}
			}
		}
	}
}
