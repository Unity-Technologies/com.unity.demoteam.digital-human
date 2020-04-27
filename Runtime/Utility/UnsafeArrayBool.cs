using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public unsafe struct UnsafeArrayBool : IDisposable
	{
		public bool* val;
		private long valSize;

		private Allocator allocator;

		public UnsafeArrayBool(int capacity, Allocator allocator = Allocator.Temp)
		{
			this.val = (bool*)UnsafeUtility.Malloc(sizeof(bool) * capacity, 1, allocator);
			this.valSize = sizeof(bool) * capacity;

			this.allocator = allocator;
		}

		public void Clear(bool value)
		{
			if (value == false)
				UnsafeUtility.MemClear(val, valSize);
			else
				UnsafeUtility.MemCpyReplicate(val, &value, sizeof(bool), (int)valSize / sizeof(bool));
		}

		public void Dispose()
		{
			if (val != null)
				UnsafeUtility.Free(val, allocator);
		}
	}
}
