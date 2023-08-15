using System;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

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

	//convinience for using LinkedIndexListArray without managed types (read only). Superficially resembles IEnumerator but does not inherit from it as it\s managed type
	public unsafe struct LinkedIndexListArrayUnsafeView
	{
		public struct LinkedIndexListArrayUnsafeViewIterator
		{
			[NativeDisableUnsafePtrRestriction, NoAlias]
			private LinkedIndexItem* items;
			private int headIndex;
			private int itemIndex;
			public LinkedIndexListArrayUnsafeViewIterator(LinkedIndexItem* itemsPtr, int head, int index)
			{
				items = itemsPtr;
				headIndex = head;
				itemIndex = index;
			}
			
			public int Current => items[itemIndex].data;

			public bool MoveNext()
			{
				if (itemIndex == -1)
				{
					itemIndex = headIndex;
					return (itemIndex != -1);//return true;
				}
				else
				{
					itemIndex = items[itemIndex].prev;
					return (itemIndex != headIndex);
				}
			}
			
			public void Reset()
			{
				itemIndex = headIndex;
			}


			
		}

		public struct LinkedIndexListArrayUnsafeViewEnumerable
		{
			[NativeDisableUnsafePtrRestriction, NoAlias]
			private LinkedIndexItem* items;
			private int headIndex;
			
			public LinkedIndexListArrayUnsafeViewEnumerable(LinkedIndexItem* itemsPtr, int head)
			{
				items = itemsPtr;
				headIndex = head;
			}
			
			
			public LinkedIndexListArrayUnsafeViewIterator GetEnumerator()
			{
				return new LinkedIndexListArrayUnsafeViewIterator(items, headIndex, -1);
			}

		}


		[NativeDisableUnsafePtrRestriction, NoAlias]
		private LinkedIndexList* lists;
		[NativeDisableUnsafePtrRestriction, NoAlias]
		private LinkedIndexItem* items;
		
		public LinkedIndexListArrayUnsafeView(LinkedIndexList* listPtr, LinkedIndexItem* itemsPtr)
		{
			lists = listPtr;
			items = itemsPtr;
		}

		public LinkedIndexListArrayUnsafeViewEnumerable this[int listIndex]
		{
			get
			{
				return new LinkedIndexListArrayUnsafeViewEnumerable(items, lists[listIndex].head);
			}
		}

	}
}
