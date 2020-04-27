using System;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public unsafe struct UnsafeArrayVector3 : IDisposable
	{
		public Vector3* val;
		private long valSize;

		private Allocator allocator;

		public UnsafeArrayVector3(int capacity, Allocator allocator = Allocator.Temp)
		{
			this.val = (Vector3*)UnsafeUtility.Malloc(sizeof(Vector3) * capacity, 1, allocator);
			this.valSize = sizeof(Vector3) * capacity;

			this.allocator = allocator;
		}

		public void Clear(Vector3 value)
		{
			if (value.x == 0.0f && value.y == 0.0f && value.z == 0.0f)
				UnsafeUtility.MemClear(val, valSize);
			else
				UnsafeUtility.MemCpyReplicate(val, &value, sizeof(Vector3), (int)valSize / sizeof(Vector3));
		}

		public void Dispose()
		{
			if (val != null)
				UnsafeUtility.Free(val, allocator);
		}
	}
}
