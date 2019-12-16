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
		public int frameOffset;
		public int frameCount;
		public int frameSize;

		public int seekRadius;

		public int ringCapacityPow2;
		public NativeArray<byte> ringData;
		public NativeArray<ReadHandle> ringDataHnd;
		public NativeArray<int> ringDataTag;

		public NativeFrameStream(string filename, int frameOffset, int frameCount, int frameSize, int seekRadius, int ringCapacity = -1)
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
					cmd.Offset = frameSize * readIndex + frameOffset;
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

	/*
	public unsafe struct NativeFrameStream2 : IDisposable
	{
		public string filename;
		public int frameCount;
		public int frameSize;

		public int streamPosition;
		public int streamReadAhead;

		public int ringCapacity;
		public int ringFrameLo;
		public int ringFrameHi;

		public NativeArray<byte> ringData;
		public NativeArray<ReadHandle> ringDataHnd;

		public NativeFrameStream2(string filename, int frameCount, int frameSize, int ringCapacity, int ringReadAhead)
		{
			this.filename = filename;
			this.frameCount = frameCount;
			this.frameSize = frameSize;

			streamPosition = -1;
			streamReadAhead = ringReadAhead;

			this.ringCapacity = ringCapacity;
			ringFrameLo = -1;
			ringFrameHi = -1;

			ringData = new NativeArray<byte>(this.ringCapacity * this.frameSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			ringDataHnd = new NativeArray<ReadHandle>(this.ringCapacity, Allocator.Persistent, NativeArrayOptions.ClearMemory);
		}

		[System.Diagnostics.Conditional("UNITY_EDITOR")]
		void ASSERT_BUFFERS()
		{
			Debug.Assert(ringData.IsCreated);
		}

		public void Dispose()
		{
			for (int i = 0; i != ringCapacity; i++)
			{
				if (ringDataHnd[i].Status == ReadStatus.InProgress)
					ringDataHnd[i].JobHandle.Complete();
			}

			ringData.Dispose();
			ringDataHnd.Dispose();
		}

		public void Seek(int frameIndex)
		{
			ASSERT_BUFFERS();

			// sanitize user input
			frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);

			// requested range will be either
			//   a) fully contained
			//   b) fully disjoint
			//   c) extending up
			//   d) extending down
			int frameLo = Mathf.Max(frameIndex - streamReadAhead, 0);
			int frameHi = Mathf.Min(frameIndex + streamReadAhead, frameCount - 1);

			var fullyContained = (frameLo >= ringFrameLo) && (frameHi <= ringFrameHi);
			if (fullyContained)
			{
				return;// already buffered
			}
			else
			{
				var fullyDisjoint = (frameLo > ringFrameHi + 1) || (frameHi < ringFrameLo - 1);
				if (fullyDisjoint)
				{
					ringFrameLo = frameLo;
					ringFrameHi = frameHi;
				}
				else
				{
					var extendingUp = frameHi > ringFrameHi;
					if (extendingUp)
					{
						frameLo = ringFrameHi + 1;
						ringFrameHi = frameHi;
						ringFrameLo = ringFrameHi - Mathf.Min(ringCapacity, ringFrameHi - ringFrameLo);
					}
					else
					{
						frameHi = ringFrameLo - 1;
						ringFrameLo = frameLo;
						ringFrameLo = ringFrameHi - Mathf.Min(ringCapacity, ringFrameHi - ringFrameLo);
					}
				}
			}

			// request each frame individually
			var ringDataPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(ringData);

			for (int streamIndex = frameLo; streamIndex <= frameHi; streamIndex++)
			{
				// wait for element in ring
				int ringIndex = (streamIndex % ringCapacity);
				if (ringDataHnd[ringIndex].Status == ReadStatus.InProgress)
					ringDataHnd[ringIndex].JobHandle.Complete();

				// schedule the read
				ReadCommand cmd;
				cmd.Buffer = ringDataPtr + frameSize * ringIndex;
				cmd.Offset = frameSize * streamIndex;
				cmd.Size = frameSize;
				ringDataHnd[ringIndex] = AsyncReadManager.Read(filename, &cmd, 1);
			}
		}

		public void* Read(int frameIndex)
		{
			ASSERT_BUFFERS();

			// sanitize user input
			frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);

			// seek if frame is not already in ring
			if (frameIndex < ringFrameLo || frameIndex > ringFrameHi)
				Seek(frameIndex);

			// wait for element in ring
			int ringIndex = (frameIndex % ringCapacity);
			if (ringDataHnd[ringIndex].Status == ReadStatus.InProgress)
				ringDataHnd[ringIndex].JobHandle.Complete();

			// return the data
			byte* ringDataPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(ringData);
			return ringDataPtr + ringIndex;
		}
	}
	//*/
}
