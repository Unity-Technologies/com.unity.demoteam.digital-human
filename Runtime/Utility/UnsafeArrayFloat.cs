using System;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public unsafe struct UnsafeArrayFloat : IDisposable
	{
		public float* val;
		private long valSize;

		private Allocator allocator;

		public UnsafeArrayFloat(int capacity, Allocator allocator = Allocator.Temp)
		{
			this.val = (float*)UnsafeUtility.Malloc(sizeof(float) * capacity, 1, allocator);
			this.valSize = sizeof(float) * capacity;

			this.allocator = allocator;
		}

		public void Clear(float value)
		{
			if (value == 0.0f)
				UnsafeUtility.MemClear(val, valSize);
			else
				UnsafeUtility.MemCpyReplicate(val, &value, sizeof(float), (int)valSize / sizeof(float));
		}

		public void Dispose()
		{
			if (val != null)
				UnsafeUtility.Free(val, allocator);
		}
	}
}
