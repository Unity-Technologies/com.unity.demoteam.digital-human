using System;
using Unity.Collections;

namespace Unity.DemoTeam.DigitalHuman
{
	public unsafe struct UnsafeBFS : IDisposable
	{
		public int position;
		public int depth;

		private UnsafeArrayULong pending;// pending[i] == (depth << 32) | position
		private int pendingHead;
		private int pendingTail;

		private UnsafeArrayBool visited;

		public UnsafeBFS(int nodeCount, Allocator allocator = Allocator.Temp)
		{
			this.position = -1;
			this.depth = -1;

			this.pending = new UnsafeArrayULong(nodeCount, allocator);
			this.pendingHead = 0;
			this.pendingTail = 0;

			this.visited = new UnsafeArrayBool(nodeCount, allocator);
			this.visited.Clear(false);
		}

		public void Dispose()
		{
			pending.Dispose();
			visited.Dispose();
		}

		public void Clear()
		{
			visited.Clear(false);
		}

		public void Ignore(int node)
		{
			visited.val[node] = true;
		}

		public void Insert(int node)
		{
			if (visited.val[node])
				return;

			ulong pack_position = (ulong)node;
			ulong pack_depth = (ulong)(depth + 1) << 32;

			pending.val[pendingTail++] = pack_depth | pack_position;
			visited.val[node] = true;
		}

		public bool MoveNext()
		{
			if (pendingHead != pendingTail)
			{
				ulong packed = pending.val[pendingHead++];
				position = (int)(packed & 0xffffffffuL);
				depth = (int)(packed >> 32);
				return true;
			}
			else
			{
				position = -1;
				depth = -1;
				return false;
			}
		}
	}
}
