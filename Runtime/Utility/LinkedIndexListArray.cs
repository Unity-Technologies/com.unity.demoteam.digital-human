using System;

namespace Unity.DemoTeam.DigitalHuman
{
	[Serializable]
	public struct LinkedIndexListArray
	{
		public LinkedIndexList[] lists;
		public LinkedIndexItem[] items;

		public int listCount;
		public int itemCount;

		public void Allocate(int listCapacity, int itemCapacity)
		{
			ArrayUtils.ResizeCheckedIfLessThan(ref lists, listCapacity);
			ArrayUtils.ResizeCheckedIfLessThan(ref items, itemCapacity);

			Clear();
		}

		public void Clear()
		{
			listCount = lists.Length;
			itemCount = 0;

			for (int i = 0; i != listCount; i++)
			{
				lists[i].head = -1;
				lists[i].size = 0;
			}
		}

		public void Append(int listIndex, int value)
		{
			if (itemCount == items.Length)
			{
				ArrayUtils.ResizeCheckedIfLessThan(ref items, items.Length * 2);
			}

			int headIndex = lists[listIndex].head;
			if (headIndex == -1)
			{
				int itemIndex = itemCount++;

				items[itemIndex] = new LinkedIndexItem
				{
					next = itemIndex,
					prev = itemIndex,
					data = value,
				};

				lists[listIndex].head = itemIndex;
				lists[listIndex].size = 1;
			}
			else
			{
				int itemIndex = itemCount++;
				int tailIndex = items[headIndex].prev;

				items[itemIndex] = new LinkedIndexItem
				{
					next = headIndex,
					prev = tailIndex,
					data = value,
				};

				items[tailIndex].next = itemIndex;
				items[headIndex].prev = itemIndex;

				lists[listIndex].size++;
			}
		}

		public void AppendMove(int listIndex, int listOther)
		{
			int headOther = lists[listOther].head;
			if (headOther != -1)
			{
				int headIndex = lists[listIndex].head;
				if (headIndex != -1)
				{
					int tailIndex = items[headIndex].prev;
					int tailOther = items[headOther].prev;

					items[headIndex].prev = tailOther;
					items[tailIndex].next = headOther;

					items[headOther].prev = tailIndex;
					items[tailOther].next = headIndex;

					lists[listIndex].size += lists[listOther].size;
				}
				else
				{
					lists[listIndex].head = lists[listOther].head;
					lists[listIndex].size = lists[listOther].size;
				}

				lists[listOther].head = -1;
				lists[listOther].size = 0;
			}
		}

		public int GetCount(int listIndex)
		{
			return lists[listIndex].size;
		}

		public LinkedIndexEnumerable this[int listIndex]
		{
			get
			{
				return new LinkedIndexEnumerable(this.items, this.lists[listIndex].head);
			}
		}
	}
}
