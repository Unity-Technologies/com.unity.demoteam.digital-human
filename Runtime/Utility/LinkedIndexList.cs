using System;
using System.Collections;
using System.Collections.Generic;

namespace Unity.DemoTeam.DigitalHuman
{
	[Serializable]
	public struct LinkedIndexList
	{
		public int head;
		public int size;
	}

	[Serializable]
	public struct LinkedIndexItem
	{
		public int next;
		public int prev;
		public int data;
	}

	[Serializable]
	public struct LinkedIndexEnumerable : IEnumerable<int>
	{
		public LinkedIndexItem[] items;
		public int headIndex;

		public LinkedIndexEnumerable(LinkedIndexItem[] items, int headIndex)
		{
			this.items = items;
			this.headIndex = headIndex;
		}

		public LinkedIndexEnumerator GetEnumerator()
		{
			return new LinkedIndexEnumerator(items, headIndex, -1);
		}

		IEnumerator<int> IEnumerable<int>.GetEnumerator()
		{
			return GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}

	[Serializable]
	public struct LinkedIndexEnumerator : IEnumerator<int>
	{
		public LinkedIndexItem[] items;
		public int headIndex;
		public int itemIndex;

		public LinkedIndexEnumerator(LinkedIndexItem[] items, int headIndex, int itemIndex)
		{
			this.items = items;
			this.headIndex = headIndex;
			this.itemIndex = itemIndex;
		}

		public int Current
		{
			get { return items[itemIndex].data; }
		}

		object IEnumerator.Current
		{
			get { return Current; }
		}

		public bool MoveNext()
		{
			if (itemIndex == -1)
			{
				itemIndex = headIndex;
				return (itemIndex != -1);//return true;
			}
			else
			{
				itemIndex = items[itemIndex].next;
				return (itemIndex != headIndex);
			}
		}

		public int ReadNext()
		{
			if (MoveNext())
				return Current;
			else
				return -1;
		}

		public void Reset()
		{
			itemIndex = headIndex;
		}

		public void Dispose()
		{
			// foo
		}
	}
}
