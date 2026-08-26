namespace Cardio.AI
{
    /// <summary>
    /// Binary heap used as A*'s open set.
    ///
    /// Worth the extra code over a plain list: the Level 1 grid holds roughly
    /// 2,800 cells, and a linear scan for the cheapest node would cost O(n) per
    /// pop, giving millions of comparisons for a single long path. The heap
    /// makes that O(log n), which is what keeps several agents re-pathing at
    /// once inside the 60 FPS budget.
    ///
    /// Allocated once at grid build and reused, so a path query produces no
    /// garbage.
    /// </summary>
    public class NodeHeap<T> where T : IHeapItem<T>
    {
        private readonly T[] _items;

        public int Count { get; private set; }

        public NodeHeap(int maxSize)
        {
            _items = new T[maxSize];
        }

        public void Add(T item)
        {
            item.HeapIndex = Count;
            _items[Count] = item;
            SortUp(item);
            Count++;
        }

        public T RemoveFirst()
        {
            T first = _items[0];
            Count--;

            _items[0] = _items[Count];
            _items[0].HeapIndex = 0;
            SortDown(_items[0]);

            return first;
        }

        /// <summary>Called after an item's cost drops, to restore heap order.</summary>
        public void UpdateItem(T item) => SortUp(item);

        public bool Contains(T item) => item.HeapIndex < Count && Equals(_items[item.HeapIndex], item);

        public void Clear() => Count = 0;

        private void SortUp(T item)
        {
            int parentIndex = (item.HeapIndex - 1) / 2;

            while (true)
            {
                T parent = _items[parentIndex];
                if (item.CompareTo(parent) <= 0) break;

                Swap(item, parent);
                parentIndex = (item.HeapIndex - 1) / 2;
            }
        }

        private void SortDown(T item)
        {
            while (true)
            {
                int leftChild = item.HeapIndex * 2 + 1;
                int rightChild = item.HeapIndex * 2 + 2;

                if (leftChild >= Count) return;

                int swapIndex = leftChild;
                if (rightChild < Count && _items[leftChild].CompareTo(_items[rightChild]) < 0) swapIndex = rightChild;

                if (item.CompareTo(_items[swapIndex]) >= 0) return;

                Swap(item, _items[swapIndex]);
            }
        }

        private void Swap(T a, T b)
        {
            _items[a.HeapIndex] = b;
            _items[b.HeapIndex] = a;

            (a.HeapIndex, b.HeapIndex) = (b.HeapIndex, a.HeapIndex);
        }
    }
}
