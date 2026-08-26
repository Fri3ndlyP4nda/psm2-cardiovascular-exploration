using System;
using UnityEngine;

namespace Cardio.AI
{
    /// <summary>Item that can live in a <see cref="NodeHeap{T}"/>.</summary>
    public interface IHeapItem<T> : IComparable<T>
    {
        int HeapIndex { get; set; }
    }

    /// <summary>
    /// One cell of the navigation grid.
    ///
    /// A class rather than a struct because A* mutates gCost/parent during a
    /// search and the heap stores references; the whole array is allocated once
    /// when the grid is built, so no garbage is produced per query.
    /// </summary>
    public class PathNode : IHeapItem<PathNode>
    {
        public readonly int GridX;
        public readonly int GridZ;

        /// <summary>Centre of the cell, at the height of the walkable surface.</summary>
        public Vector3 WorldPosition;

        /// <summary>False for walls, obstacles, missing floor, or ledges too high to stand on.</summary>
        public bool Walkable;

        /// <summary>
        /// Extra traversal cost, used to make agents prefer open ground over
        /// squeezing along a wall. Keeps paths looking deliberate rather than
        /// scraping every corner.
        /// </summary>
        public int Penalty;

        // ---- A* working state, reset per search ----
        public int GCost;
        public int HCost;
        public PathNode Parent;

        public int FCost => GCost + HCost;

        public int HeapIndex { get; set; }

        public PathNode(int gridX, int gridZ, Vector3 worldPosition, bool walkable)
        {
            GridX = gridX;
            GridZ = gridZ;
            WorldPosition = worldPosition;
            Walkable = walkable;
        }

        /// <summary>
        /// Heap ordering: lowest F first, breaking ties on the lower H so the
        /// search pushes towards the goal instead of fanning out sideways.
        /// Returns the comparison inverted because NodeHeap is a max-heap
        /// implementation used as a min-heap.
        /// </summary>
        public int CompareTo(PathNode other)
        {
            int compare = FCost.CompareTo(other.FCost);
            if (compare == 0) compare = HCost.CompareTo(other.HCost);

            return -compare;
        }
    }
}
