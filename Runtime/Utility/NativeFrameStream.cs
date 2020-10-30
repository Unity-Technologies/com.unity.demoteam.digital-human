//#define VERBOSE

using System;
using Unity.IO.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	public unsafe struct NativeFrameStream : IDisposable
	{
		public string filename;
		public long frameOffset;
		public int frameCount;
		public int frameSize;

		public int seekRadius;

		public int ringCapacityPow2;
		public NativeArray<byte> ringData;
		public NativeArray<ReadHandle> ringDataHnd;
		public NativeArray<int> ringDataTag;

		public NativeFrameStream(string filename, long frameOffset, int frameCount, int frameSize, int seekRadius, int ringCapacity = -1)
		{
			this.filename = filename;
			this.frameOffset = frameOffset;
			this.frameCount = frameCount;
			this.frameSize = frameSize;

			this.seekRadius = seekRadius;

			ringCapacityPow2 = math.ceilpow2(math.max(ringCapacity, 1 + 2 * seekRadius));
			ringData = new NativeArray<byte>(ringCapacityPow2 * frameSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			ringDataHnd = new NativeArray<ReadHandle>(ringCapacityPow2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
			ringDataTag = new NativeArray<int>(ringCapacityPow2, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

			for (int i = 0; i != ringCapacityPow2; i++)
			{
				ringDataTag[i] = -1;
			}
		}

		[System.Diagnostics.Conditional("UNITY_EDITOR")]
		void ASSERT_BUFFERS()
		{
			Debug.Assert(ringData.IsCreated);
		}

		public void Dispose()
		{
			if (ringDataHnd.IsCreated)
			{
				// ensure that all pending reads have completed
				for (int i = 0; i != ringCapacityPow2; i++)
				{
					var dataHnd = ringDataHnd[i];
					if (dataHnd.IsValid() && dataHnd.Status == ReadStatus.InProgress)
						dataHnd.JobHandle.Complete();
				}

				ringDataHnd.Dispose();
			}

			if (ringDataTag.IsCreated)
				ringDataTag.Dispose();

			if (ringData.IsCreated)
				ringData.Dispose();
		}

		void WaitForRingIndex(int ringIndex)
		{

		}

		public void SeekFrame(int frameIndex)
		{
			ASSERT_BUFFERS();

#if VERBOSE
			Debug.Log("SEEK " + frameIndex);
#endif

			// sanitize user input
			frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);

			// expand range within bounds
			int readIndexLo = Mathf.Max(frameIndex - seekRadius, 0);
			int readIndexHi = Mathf.Min(frameIndex + seekRadius, frameCount - 1);

			// schedule each read individually
			var ringDataPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(ringData);
			for (int readIndex = readIndexLo; readIndex <= readIndexHi; readIndex++)
			{
				// map frame to index in ring
				int ringIndex = (readIndex & (ringCapacityPow2 - 1));

				// if frame is not already in ring
				if (ringDataTag[ringIndex] != readIndex)
				{
					ringDataTag[ringIndex] = readIndex;

					// wait for pending element operation
					var dataHnd = ringDataHnd[ringIndex];
					if (dataHnd.IsValid() && dataHnd.Status == ReadStatus.InProgress)
						dataHnd.JobHandle.Complete();

					// schedule the read
					ReadCommand cmd;
					cmd.Buffer = frameSize * ringIndex + ringDataPtr;
					cmd.Offset = frameSize * (long)readIndex + frameOffset;
					cmd.Size = frameSize;
					ringDataHnd[ringIndex] = AsyncReadManager.Read(filename, &cmd, 1);

#if VERBOSE
					Debug.Log("schedule " + frameIndex + " -> ringIndex " + ringIndex);
#endif
				}
			}
		}

		public void* ReadFrame(int frameIndex)
		{
			ASSERT_BUFFERS();

			// sanitize user input
			frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);

			// map frame to index in ring
			int ringIndex = (frameIndex & (ringCapacityPow2 - 1));

#if VERBOSE
			Debug.Log("read " + frameIndex + " -> ringIndex " + ringIndex);
#endif

			// seek if frame is not already in ring
			if (ringDataTag[ringIndex] != frameIndex)
			{
				SeekFrame(frameIndex);
			}

			// wait for pending element operation
			var dataHnd = ringDataHnd[ringIndex];
			if (dataHnd.IsValid() && dataHnd.Status == ReadStatus.InProgress)
				dataHnd.JobHandle.Complete();

			// return the data
			byte* ringDataPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(ringData);
			return ringDataPtr + ringIndex * frameSize;
		}
	}
}
