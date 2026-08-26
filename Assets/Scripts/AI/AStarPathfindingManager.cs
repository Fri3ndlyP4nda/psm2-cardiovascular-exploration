using System.Collections.Generic;
using Cardio.Core;
using UnityEngine;

namespace Cardio.AI
{
    /// <summary>
    /// The A* navigation grid and search, as required by PSM1 section 13.
    ///
    /// GRID SHAPE - why 2.5D rather than a full voxel volume:
    /// every agent in this game is ground-based (they walk the chamber floor
    /// under gravity, exactly like the player). A full 3D voxel grid would
    /// therefore spend memory and search time on cells no agent can ever
    /// occupy. Instead the grid is a 2D lattice over XZ where each cell is
    /// *sampled against the real 3D scene*: a downward raycast finds the floor
    /// height, and a clearance sphere at body height rejects anything a body
    /// could not fit through. That is what makes the grid account for the 3D
    /// environment - chamber walls, papillary muscles, the septum and fatty
    /// plaque all remove cells - while staying cheap enough to rebuild instantly.
    ///
    /// Ledges are rejected by <see cref="maxWalkableHeight"/>: without it, a ray
    /// landing on the top of an 11-unit chamber wall would report perfectly good
    /// "ground" and agents would happily path along the battlements.
    ///
    /// The algorithm itself is textbook A* with an octile heuristic and
    /// corner-cutting prevention. Per PSM1 section 14 it is NOT modified by the
    /// difficulty system - the DDA changes how fast agents move, never how they
    /// find their way.
    /// </summary>
    [DisallowMultipleComponent]
    public class AStarPathfindingManager : MonoBehaviour
    {
        public static AStarPathfindingManager Instance { get; private set; }

        [Header("Grid bounds (centred on this transform)")]
        [SerializeField] private Vector2 gridWorldSize = new Vector2(46f, 62f);
        [SerializeField, Range(0.25f, 3f)] private float nodeRadius = 0.5f;

        [Header("Sampling")]
        [Tooltip("Layers treated as standable floor.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Layers that block an agent's body: walls, muscles, plaque.")]
        [SerializeField] private LayerMask blockingMask = ~0;

        [Tooltip("Surfaces higher than this are ledges, not floor. Keeps agents off wall tops.")]
        [SerializeField] private float maxWalkableHeight = 1.5f;

        [Tooltip("Body radius used for the clearance test. Larger values keep agents further from walls.")]
        [SerializeField, Range(0.1f, 2f)] private float agentRadius = 0.55f;

        [Tooltip("Height above the floor at which clearance is tested.")]
        [SerializeField, Range(0.1f, 3f)] private float clearanceHeight = 0.7f;

        [Header("Path quality")]
        [Tooltip("Extra cost for cells next to an unwalkable cell, so paths avoid scraping walls.")]
        [SerializeField, Range(0, 50)] private int wallProximityPenalty = 12;

        [Tooltip("Collapse waypoints that continue in the same direction.")]
        [SerializeField] private bool simplifyPaths = true;

        [Header("Debug")]
        [SerializeField] private bool drawGridGizmos;
        [SerializeField] private bool drawPathGizmos = true;

        private PathNode[,] _grid;
        private int _gridSizeX;
        private int _gridSizeZ;
        private NodeHeap<PathNode> _openSet;
        private readonly HashSet<PathNode> _closedSet = new HashSet<PathNode>();

        /// <summary>Most recent successful path, kept only for gizmo drawing.</summary>
        private readonly List<Vector3> _lastPath = new List<Vector3>();

        public bool IsBuilt => _grid != null;
        public int WalkableNodeCount { get; private set; }
        public int TotalNodeCount => _gridSizeX * _gridSizeZ;

        /// <summary>Diagnostic: how many nodes the last search expanded.</summary>
        public int LastSearchExpandedNodes { get; private set; }

        private float NodeDiameter => nodeRadius * 2f;

        private void Awake()
        {
            Instance = this;
            BuildGrid();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // Grid construction
        // ------------------------------------------------------------------

        /// <summary>
        /// Samples the scene and rebuilds every cell. Safe to call from editor
        /// tooling as well as at runtime.
        /// </summary>
        public void BuildGrid()
        {
            _gridSizeX = Mathf.Max(1, Mathf.RoundToInt(gridWorldSize.x / NodeDiameter));
            _gridSizeZ = Mathf.Max(1, Mathf.RoundToInt(gridWorldSize.y / NodeDiameter));
            _grid = new PathNode[_gridSizeX, _gridSizeZ];
            _openSet = new NodeHeap<PathNode>(_gridSizeX * _gridSizeZ);

            Vector3 bottomLeft = transform.position
                                 - Vector3.right * (gridWorldSize.x * 0.5f)
                                 - Vector3.forward * (gridWorldSize.y * 0.5f);

            WalkableNodeCount = 0;

            for (int x = 0; x < _gridSizeX; x++)
            {
                for (int z = 0; z < _gridSizeZ; z++)
                {
                    Vector3 column = bottomLeft
                                     + Vector3.right * (x * NodeDiameter + nodeRadius)
                                     + Vector3.forward * (z * NodeDiameter + nodeRadius);

                    bool walkable = SampleCell(column, out Vector3 surface);
                    _grid[x, z] = new PathNode(x, z, surface, walkable);

                    if (walkable) WalkableNodeCount++;
                }
            }

            if (wallProximityPenalty > 0) ApplyProximityPenalties();
        }

        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];

        /// <summary>
        /// Decides whether one cell can be stood in, and where its floor is.
        ///
        /// Every surface in the column is collected rather than just the first,
        /// then the highest one still low enough to stand on is chosen. Taking
        /// the first hit from above would find the top of any overhead geometry
        /// and wrongly conclude the cell is an unreachable ledge - which is
        /// exactly what happened at the mitral and aortic valves, whose annulus
        /// arches over the opening five units above the agent's head and sealed
        /// the chamber off from both corridors.
        /// </summary>
        private bool SampleCell(Vector3 column, out Vector3 surface)
        {
            const float sampleFromHeight = 40f;
            const float sampleDistance = 80f;

            var origin = new Vector3(column.x, transform.position.y + sampleFromHeight, column.z);
            surface = new Vector3(column.x, transform.position.y, column.z);

            int count = Physics.RaycastNonAlloc(new Ray(origin, Vector3.down), GroundHits, sampleDistance,
                                                groundMask, QueryTriggerInteraction.Ignore);
            if (count == 0) return false;

            // 1. Highest standable floor in this column.
            bool foundFloor = false;
            float bestHeight = float.MinValue;

            for (int i = 0; i < count; i++)
            {
                float height = GroundHits[i].point.y;

                // Ledges and overhead beams are not floor an agent can use.
                if (height - transform.position.y > maxWalkableHeight) continue;
                if (height <= bestHeight) continue;

                bestHeight = height;
                surface = GroundHits[i].point;
                foundFloor = true;
            }

            if (!foundFloor) return false;

            // 2. Room for a body above that floor? This is what still rejects
            //    cells inside walls, since the floor beneath a wall is found but
            //    the wall itself occupies the space above it.
            Vector3 bodyCentre = surface + Vector3.up * clearanceHeight;
            if (Physics.CheckSphere(bodyCentre, agentRadius, blockingMask, QueryTriggerInteraction.Ignore)) return false;

            return true;
        }

        /// <summary>Adds cost near unwalkable cells so paths keep a little clearance.</summary>
        private void ApplyProximityPenalties()
        {
            for (int x = 0; x < _gridSizeX; x++)
            {
                for (int z = 0; z < _gridSizeZ; z++)
                {
                    PathNode node = _grid[x, z];
                    if (!node.Walkable) continue;

                    foreach (PathNode neighbour in GetNeighbours(node))
                    {
                        if (neighbour.Walkable) continue;

                        node.Penalty = wallProximityPenalty;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Marks the cells overlapping a world bounds as unwalkable (or restores
        /// them). Used when a blockade appears or clears at runtime, so A* has
        /// to find an alternate route without a full grid rebuild.
        /// </summary>
        public void SetRegionBlocked(Bounds bounds, bool blocked)
        {
            if (!IsBuilt) return;

            for (int x = 0; x < _gridSizeX; x++)
            {
                for (int z = 0; z < _gridSizeZ; z++)
                {
                    PathNode node = _grid[x, z];

                    var flat = new Vector3(node.WorldPosition.x, bounds.center.y, node.WorldPosition.z);
                    if (!bounds.Contains(flat)) continue;

                    node.Walkable = blocked ? false : SampleCell(node.WorldPosition, out _);
                }
            }
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        public PathNode NodeFromWorldPoint(Vector3 worldPosition)
        {
            if (!IsBuilt) return null;

            float percentX = Mathf.Clamp01((worldPosition.x - transform.position.x + gridWorldSize.x * 0.5f) / gridWorldSize.x);
            float percentZ = Mathf.Clamp01((worldPosition.z - transform.position.z + gridWorldSize.y * 0.5f) / gridWorldSize.y);

            int x = Mathf.Clamp(Mathf.RoundToInt((_gridSizeX - 1) * percentX), 0, _gridSizeX - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((_gridSizeZ - 1) * percentZ), 0, _gridSizeZ - 1);

            return _grid[x, z];
        }

        /// <summary>
        /// Nearest walkable node to a point, searched in rings.
        ///
        /// This is what stops an agent becoming permanently stuck: if it is
        /// nudged half inside geometry, or the target stands somewhere
        /// unreachable, the search still gets a sensible start and goal instead
        /// of failing outright.
        /// </summary>
        public PathNode NearestWalkableNode(Vector3 worldPosition, int maxRingSearch = 12)
        {
            PathNode start = NodeFromWorldPoint(worldPosition);
            if (start == null || start.Walkable) return start;

            for (int ring = 1; ring <= maxRingSearch; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dz = -ring; dz <= ring; dz++)
                    {
                        // Only the perimeter of the ring; the interior was covered already.
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring) continue;

                        int x = start.GridX + dx;
                        int z = start.GridZ + dz;
                        if (x < 0 || x >= _gridSizeX || z < 0 || z >= _gridSizeZ) continue;

                        if (_grid[x, z].Walkable) return _grid[x, z];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Standard A*. Fills <paramref name="waypoints"/> and returns true when
        /// a route exists.
        /// </summary>
        public bool FindPath(Vector3 startPosition, Vector3 targetPosition, List<Vector3> waypoints)
        {
            waypoints.Clear();
            LastSearchExpandedNodes = 0;

            if (!IsBuilt) return false;

            PathNode startNode = NearestWalkableNode(startPosition);
            PathNode targetNode = NearestWalkableNode(targetPosition);
            if (startNode == null || targetNode == null) return false;

            if (startNode == targetNode)
            {
                waypoints.Add(targetNode.WorldPosition);
                return true;
            }

            _openSet.Clear();
            _closedSet.Clear();

            startNode.GCost = 0;
            startNode.HCost = Heuristic(startNode, targetNode);
            startNode.Parent = null;
            _openSet.Add(startNode);

            while (_openSet.Count > 0)
            {
                PathNode current = _openSet.RemoveFirst();
                _closedSet.Add(current);
                LastSearchExpandedNodes++;

                if (current == targetNode)
                {
                    RetracePath(startNode, targetNode, waypoints);
                    return true;
                }

                foreach (PathNode neighbour in GetNeighbours(current))
                {
                    if (!neighbour.Walkable || _closedSet.Contains(neighbour)) continue;
                    if (IsDiagonalBlocked(current, neighbour)) continue;

                    int tentativeG = current.GCost + Heuristic(current, neighbour) + neighbour.Penalty;
                    bool inOpen = _openSet.Contains(neighbour);

                    if (!inOpen || tentativeG < neighbour.GCost)
                    {
                        neighbour.GCost = tentativeG;
                        neighbour.HCost = Heuristic(neighbour, targetNode);
                        neighbour.Parent = current;

                        if (!inOpen) _openSet.Add(neighbour);
                        else _openSet.UpdateItem(neighbour);
                    }
                }
            }

            return false;   // exhausted the reachable area without reaching the goal
        }

        /// <summary>
        /// Octile distance: 14 diagonal, 10 orthogonal. Admissible on an
        /// 8-connected grid, so A* is guaranteed to return the cheapest route.
        /// </summary>
        private static int Heuristic(PathNode a, PathNode b)
        {
            int distX = Mathf.Abs(a.GridX - b.GridX);
            int distZ = Mathf.Abs(a.GridZ - b.GridZ);

            return distX > distZ
                ? 14 * distZ + 10 * (distX - distZ)
                : 14 * distX + 10 * (distZ - distX);
        }

        /// <summary>
        /// Blocks diagonal moves that would clip a corner. Without this, an
        /// agent squeezes through the join between two walls - which PSM1
        /// explicitly forbids.
        /// </summary>
        private bool IsDiagonalBlocked(PathNode from, PathNode to)
        {
            int dx = to.GridX - from.GridX;
            int dz = to.GridZ - from.GridZ;
            if (dx == 0 || dz == 0) return false;

            PathNode sideA = GetNode(from.GridX + dx, from.GridZ);
            PathNode sideB = GetNode(from.GridX, from.GridZ + dz);

            return sideA == null || sideB == null || !sideA.Walkable || !sideB.Walkable;
        }

        private PathNode GetNode(int x, int z)
        {
            if (x < 0 || x >= _gridSizeX || z < 0 || z >= _gridSizeZ) return null;
            return _grid[x, z];
        }

        private IEnumerable<PathNode> GetNeighbours(PathNode node)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;

                    PathNode neighbour = GetNode(node.GridX + dx, node.GridZ + dz);
                    if (neighbour != null) yield return neighbour;
                }
            }
        }

        private void RetracePath(PathNode start, PathNode end, List<Vector3> waypoints)
        {
            var reversed = new List<PathNode>();

            PathNode current = end;
            while (current != start && current != null)
            {
                reversed.Add(current);
                current = current.Parent;
            }

            reversed.Reverse();

            if (simplifyPaths) Simplify(reversed, waypoints);
            else foreach (PathNode node in reversed) waypoints.Add(node.WorldPosition);

            _lastPath.Clear();
            _lastPath.AddRange(waypoints);
        }

        /// <summary>Keeps only the cells where the direction of travel changes.</summary>
        private static void Simplify(List<PathNode> nodes, List<Vector3> waypoints)
        {
            Vector2 previousDirection = Vector2.zero;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (i > 0)
                {
                    var direction = new Vector2(nodes[i - 1].GridX - nodes[i].GridX,
                                                nodes[i - 1].GridZ - nodes[i].GridZ);

                    if (direction == previousDirection && i < nodes.Count - 1) continue;
                    previousDirection = direction;
                }

                waypoints.Add(nodes[i].WorldPosition);
            }
        }

        // ------------------------------------------------------------------
        // Gizmos
        // ------------------------------------------------------------------

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireCube(transform.position, new Vector3(gridWorldSize.x, 0.2f, gridWorldSize.y));

            if (drawGridGizmos && _grid != null)
            {
                foreach (PathNode node in _grid)
                {
                    Gizmos.color = node.Walkable
                        ? new Color(0.3f, 0.8f, 0.4f, 0.18f)
                        : new Color(0.9f, 0.25f, 0.25f, 0.28f);

                    Gizmos.DrawCube(node.WorldPosition + Vector3.up * 0.05f,
                                    new Vector3(NodeDiameter * 0.9f, 0.05f, NodeDiameter * 0.9f));
                }
            }

            if (!drawPathGizmos || _lastPath.Count < 2) return;

            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            for (int i = 0; i < _lastPath.Count - 1; i++)
            {
                Gizmos.DrawLine(_lastPath[i] + Vector3.up * 0.3f, _lastPath[i + 1] + Vector3.up * 0.3f);
                Gizmos.DrawSphere(_lastPath[i] + Vector3.up * 0.3f, 0.15f);
            }
        }
    }
}
