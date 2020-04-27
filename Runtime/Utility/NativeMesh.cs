using System;
using UnityEngine;
using Unity.Collections;

namespace Unity.DemoTeam.DigitalHuman
{
	public struct NativeMeshSOA : IDisposable
	{
		public NativeArray<Vector3> vertexPositions;
		public NativeArray<Vector2> vertexTexCoords;
		public NativeArray<Vector3> vertexNormals;
		public int vertexCount;

		public NativeArray<int> faceIndices;
		public int faceIndicesCount;

		public void Dispose()
		{
			vertexPositions.Dispose();
			vertexTexCoords.Dispose();
			vertexNormals.Dispose();
			vertexCount = 0;

			faceIndices.Dispose();
			faceIndicesCount = 0;
		}
	}
}
