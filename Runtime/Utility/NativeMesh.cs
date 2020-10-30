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

		public NativeMeshSOA(NativeMeshSOA other, Allocator allocator)
		{
			vertexPositions = new NativeArray<Vector3>(other.vertexPositions, allocator);
			vertexTexCoords = new NativeArray<Vector2>(other.vertexTexCoords, allocator);
			vertexNormals = new NativeArray<Vector3>(other.vertexNormals, allocator);
			vertexCount = other.vertexCount;

			faceIndices = new NativeArray<int>(other.faceIndices, allocator);
			faceIndicesCount = other.faceIndicesCount;
		}

		public void Allocate(int vertexCount, int faceIndicesCount, Allocator allocator)
		{
			this.vertexPositions = new NativeArray<Vector3>(vertexCount, allocator);
			this.vertexTexCoords = new NativeArray<Vector2>(vertexCount, allocator);
			this.vertexNormals = new NativeArray<Vector3>(vertexCount, allocator);
			this.vertexCount = vertexCount;

			this.faceIndices = new NativeArray<int>(faceIndicesCount, allocator);
			this.faceIndicesCount = faceIndicesCount;
		}

		public void CopyFrom(NativeMeshSOA other)
		{
			Debug.Assert(other.vertexCount == this.vertexCount);
			Debug.Assert(other.faceIndicesCount == this.faceIndicesCount);

			this.vertexPositions.CopyFrom(other.vertexPositions);
			this.vertexTexCoords.CopyFrom(other.vertexTexCoords);
			this.vertexNormals.CopyFrom(other.vertexNormals);
			this.vertexCount = other.vertexCount;

			this.faceIndices.CopyFrom(other.faceIndices);
			this.faceIndicesCount = other.faceIndicesCount;
		}

		public void CopyTo(NativeMeshSOA other)
		{
			Debug.Assert(other.vertexCount == this.vertexCount);
			Debug.Assert(other.faceIndicesCount == this.faceIndicesCount);

			other.vertexPositions.CopyFrom(this.vertexPositions);
			other.vertexTexCoords.CopyFrom(this.vertexTexCoords);
			other.vertexNormals.CopyFrom(this.vertexNormals);
			other.vertexCount = this.vertexCount;

			other.faceIndices.CopyFrom(this.faceIndices);
			other.faceIndicesCount = this.faceIndicesCount;
		}
	}
}
