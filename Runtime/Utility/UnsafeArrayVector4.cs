using System;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
    public unsafe struct UnsafeArrayVector4 : IDisposable
    {
        public Vector4* val;
        private long valSize;

        private Allocator allocator;

        public UnsafeArrayVector4(int capacity, Allocator allocator = Allocator.Temp)
        {
            this.val = (Vector4*)UnsafeUtility.Malloc(sizeof(Vector4) * capacity, 1, allocator);
            this.valSize = sizeof(Vector4) * capacity;

            this.allocator = allocator;
        }

        public void Clear(Vector4 value)
        {
            if (value.x == 0.0f && value.y == 0.0f && value.z == 0.0f)
                UnsafeUtility.MemClear(val, valSize);
            else
                UnsafeUtility.MemCpyReplicate(val, &value, sizeof(Vector4), (int)valSize / sizeof(Vector4));
        }

        public void Dispose()
        {
            if (val != null)
                UnsafeUtility.Free(val, allocator);
        }
    }
}