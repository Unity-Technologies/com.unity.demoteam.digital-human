using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public unsafe struct UnsafeArrayInt : IDisposable
	{
		public int* val;
		private long valSize;

		private Allocator allocator;

		public UnsafeArrayInt(int capacity, Allocator allocator = Allocator.Temp)
		{
			this.val = (int*)UnsafeUtility.Malloc(sizeof(int) * capacity, 1, allocator);
			this.valSize = sizeof(int) * capacity;

			this.allocator = allocator;
		}

		public void Clear(int value)
		{
			if (value == 0)
				UnsafeUtility.MemClear(val, valSize);
			else
				UnsafeUtility.MemCpyReplicate(val, &value, sizeof(int), (int)valSize / sizeof(int));
		}

		public void Dispose()
		{
			if (val != null)
				UnsafeUtility.Free(val, allocator);
		}
	}
}
