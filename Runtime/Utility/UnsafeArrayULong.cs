using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public unsafe struct UnsafeArrayULong : IDisposable
	{
		public ulong* val;
		private long valSize;

		private Allocator allocator;

		public UnsafeArrayULong(int capacity, Allocator allocator = Allocator.Temp)
		{
			this.val = (ulong*)UnsafeUtility.Malloc(sizeof(ulong) * capacity, 1, allocator);
			this.valSize = sizeof(ulong) * capacity;

			this.allocator = allocator;
		}

		public void Clear(ulong value)
		{
			if (value == 0uL)
				UnsafeUtility.MemClear(val, valSize);
			else
				UnsafeUtility.MemCpyReplicate(val, &value, sizeof(ulong), (int)valSize / sizeof(ulong));
		}

		public void Dispose()
		{
			if (val != null)
				UnsafeUtility.Free(val, allocator);
		}
	}
}
