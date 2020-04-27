using Unity.Collections;
using Unity.Mathematics;

namespace Unity.DemoTeam.DigitalHuman
{
	public static class ArrayUtils
	{
		public static void ResizeChecked<T>(ref T[] array, int length)
		{
			if (array == null)
				array = new T[length];
			if (array.Length != length)
				System.Array.Resize(ref array, length);
		}

		public static void ResizeCheckedIfLessThan<T>(ref T[] array, int length)
		{
			if (array == null)
				array = new T[length];
			if (array.Length < length)
				System.Array.Resize(ref array, length);
		}

		public static void CopyChecked<T>(T[] arraySrc, ref T[] arrayDst, int length)
		{
			ResizeCheckedIfLessThan(ref arrayDst, length);
			System.Array.Copy(arraySrc, arrayDst, length);
		}

		public static void ClearChecked<T>(T[] array)
		{
			if (array != null)
				System.Array.Clear(array, 0, array.Length);
		}

		public static void Realloc<T>(ref NativeArray<T> array, int length, Allocator allocator = Allocator.Persistent) where T : struct
		{
			NativeArray<T> dst = new NativeArray<T>(length, allocator, NativeArrayOptions.UninitializedMemory);
			NativeArray<T>.Copy(array, dst, math.min(array.Length, length));
			array.Dispose();
			array = dst;
		}

		public static void ReallocChecked<T>(ref NativeArray<T> array, int length, Allocator allocator = Allocator.Persistent) where T : struct
		{
			if (array.IsCreated == false)
				array = new NativeArray<T>(length, allocator, NativeArrayOptions.UninitializedMemory);
			if (array.Length != length)
				Realloc(ref array, length, allocator);
		}

		public static void ReallocCheckedIfLessThan<T>(ref NativeArray<T> array, int length, Allocator allocator = Allocator.Persistent) where T : struct
		{
			if (array.IsCreated == false)
				array = new NativeArray<T>(length, allocator, NativeArrayOptions.UninitializedMemory);
			if (array.Length < length)
				Realloc(ref array, length, allocator);
		}

		public static void CopyChecked<T>(in NativeArray<T> arraySrc, ref NativeArray<T> arrayDst, Allocator allocator = Allocator.Persistent) where T : struct
		{
			ReallocCheckedIfLessThan(ref arrayDst, arraySrc.Length, allocator);
			NativeArray<T>.Copy(arraySrc, arrayDst);
		}
	}
}
