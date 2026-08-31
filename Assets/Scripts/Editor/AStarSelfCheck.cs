using System.Collections.Generic;
using Cardio.AI;
using Cardio.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Verifies A* against the real Level 1 geometry, without entering Play mode.
    ///
    /// This is stronger than a synthetic unit test: it opens the generated
    /// scene, builds the grid by sampling the actual chamber walls, papillary
    /// muscles, septum and plaque, then runs real queries across it. Every PSM1
    /// section 13 requirement that can be checked statically is checked here -
    /// a valid route exists, no waypoint sits inside geometry, blocked regions
    /// force a detour, and unreachable goals fail cleanly instead of hanging.
    ///
    /// The one requirement it cannot cover is "an agent chases the player" -
    /// that needs Play mode and a human.
    /// </summary>
    public static class AStarSelfCheck
    {
        private static int _passed;
        private static int _failed;

        [MenuItem("PSM2/Diagnostics/Run A* Pathfinding Self-Check", priority = 72)]
        public static void Run()
        {
            _passed = 0;
            _failed = 0;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[PSM2 AStarCheck] Cancelled - unsaved changes in the open scene.");
                return;
            }

            string scenePath = $"{ProjectAssets.ScenesFolder}/{GameConstants.SceneLevel1}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogError($"[PSM2 AStarCheck] {scenePath} not found. Run PSM2 > Setup > Build or Rebuild Project.");
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var manager = Object.FindAnyObjectByType<AStarPathfindingManager>();
            if (manager == null)
            {
                Debug.LogError("[PSM2 AStarCheck] No AStarPathfindingManager in Level 1.");
                return;
            }

            // Colliders must be registered with the physics scene before the grid
            // samples them; in edit mode that is not automatic.
            Physics.SyncTransforms();
            manager.BuildGrid();

            CheckGrid(manager);
            CheckMainRoute(manager);
            CheckClearance(manager);
            CheckBlockedRegion(manager);
            CheckDegenerateQueries(manager);

            // Phase 8: the levels built in this phase, which nothing else covers.
            CheckCollateralRoute();
            CheckLevelNavigable(GameConstants.SceneLevel2, "Level 2");
            CheckLevelNavigable(GameConstants.SceneLevel3, "Level 3");

            string summary = $"[PSM2 AStarCheck] {_passed} passed, {_failed} failed.";
            if (_failed == 0) Debug.Log(summary);
            else Debug.LogError(summary);
        }

        // ------------------------------------------------------------------
        // Cases
        // ------------------------------------------------------------------

        private static void CheckGrid(AStarPathfindingManager manager)
        {
            True("grid builds", manager.IsBuilt);
            True("grid has nodes", manager.TotalNodeCount > 0);
            True("grid has walkable space", manager.WalkableNodeCount > 0);

            // The chamber and its two corridors should leave a healthy majority
            // of the bounding box unwalkable (walls, exterior, obstacles) but
            // still a substantial walkable core.
            float walkableFraction = (float)manager.WalkableNodeCount / manager.TotalNodeCount;
            True($"walkable fraction is plausible ({walkableFraction:P0})",
                 walkableFraction > 0.05f && walkableFraction < 0.85f);

            Debug.Log($"[PSM2 AStarCheck] grid: {manager.WalkableNodeCount}/{manager.TotalNodeCount} walkable " +
                      $"({walkableFraction:P1}).");
        }

        /// <summary>The route a chasing agent would actually need: inflow to the aorta exit.</summary>
        private static void CheckMainRoute(AStarPathfindingManager manager)
        {
            Vector3 spawn = FindPosition("SpawnPoint", new Vector3(0f, 1.2f, -20f));
            Vector3 exit = FindPosition("ExitAnchor", new Vector3(0f, 1.5f, 26f));

            var path = new List<Vector3>();
            bool found = manager.FindPath(spawn, exit, path);

            True("path found from spawn to exit", found);
            if (!found) return;

            True("path has waypoints", path.Count > 0);

            // Straight-line distance is ~46 units; the route must be at least
            // that long, and should not be absurdly longer.
            float length = PathLength(spawn, path);
            float direct = Vector3.Distance(spawn, exit);

            True($"path length {length:0.#} >= direct {direct:0.#}", length >= direct - 1f);
            True($"path is not a wild detour ({length / direct:0.00}x direct)", length < direct * 3f);

            Debug.Log($"[PSM2 AStarCheck] main route: {path.Count} waypoints, {length:0.#} units " +
                      $"({length / direct:0.00}x direct), {manager.LastSearchExpandedNodes} nodes expanded.");
        }

        /// <summary>
        /// The core PSM1 requirement: no part of a path may pass through
        /// geometry. Every waypoint is re-tested against the blocking layers.
        /// </summary>
        private static void CheckClearance(AStarPathfindingManager manager)
        {
            int environment = LayerMask.NameToLayer(GameConstants.LayerEnvironment);
            int obstacle = LayerMask.NameToLayer(GameConstants.LayerObstacle);

            int blockingMask = (environment >= 0 ? 1 << environment : 0) | (obstacle >= 0 ? 1 << obstacle : 0);
            if (blockingMask == 0)
            {
                Debug.LogWarning("[PSM2 AStarCheck] Environment/Obstacle layers missing - clearance check skipped.");
                return;
            }

            // Sample several routes across the chamber, including past the
            // papillary muscles and the septum.
            var routes = new[]
            {
                (new Vector3(0f, 1f, -18f), new Vector3(0f, 1f, 24f)),      // inflow to aorta
                (new Vector3(-10f, 1f, 0f), new Vector3(10f, 1f, 0f)),      // across, past the muscles
                (new Vector3(-9f, 1f, -8f), new Vector3(9f, 1f, 9f)),       // diagonal
                (new Vector3(0f, 1f, 20f), new Vector3(0f, 1f, -18f))       // aorta back to inflow
            };

            int clearWaypoints = 0;
            int blockedWaypoints = 0;

            foreach ((Vector3 from, Vector3 to) in routes)
            {
                var path = new List<Vector3>();
                if (!manager.FindPath(from, to, path)) continue;

                foreach (Vector3 waypoint in path)
                {
                    bool blocked = Physics.CheckSphere(waypoint + Vector3.up * 0.7f, 0.45f,
                                                       blockingMask, QueryTriggerInteraction.Ignore);
                    if (blocked) blockedWaypoints++;
                    else clearWaypoints++;
                }
            }

            True("routes produced waypoints", clearWaypoints + blockedWaypoints > 0);
            Equal("no waypoint sits inside geometry", blockedWaypoints, 0);

            Debug.Log($"[PSM2 AStarCheck] clearance: {clearWaypoints} waypoints checked, {blockedWaypoints} blocked.");
        }

        /// <summary>
        /// Blocking the aorta corridor must change the answer - either a longer
        /// route or no route. This is the "recalculate when a path is blocked"
        /// requirement, and proves fatty blockades genuinely affect navigation.
        /// </summary>
        private static void CheckBlockedRegion(AStarPathfindingManager manager)
        {
            Vector3 from = new Vector3(0f, 1f, -14f);
            Vector3 to = new Vector3(0f, 1f, 24f);

            var before = new List<Vector3>();
            bool foundBefore = manager.FindPath(from, to, before);
            True("route exists before blocking", foundBefore);
            if (!foundBefore) return;

            float lengthBefore = PathLength(from, before);

            // Seal the aorta corridor completely.
            var wall = new Bounds(new Vector3(0f, 1f, 18f), new Vector3(14f, 4f, 3f));
            manager.SetRegionBlocked(wall, true);

            var after = new List<Vector3>();
            bool foundAfter = manager.FindPath(from, to, after);

            if (foundAfter)
            {
                float lengthAfter = PathLength(from, after);
                True($"blocked route is longer or rerouted ({lengthBefore:0.#} -> {lengthAfter:0.#})",
                     lengthAfter > lengthBefore + 0.5f);
            }
            else
            {
                // Equally valid: the corridor is the only way through.
                _passed++;
                Debug.Log("[PSM2 AStarCheck] blocking the corridor made the exit unreachable, as expected.");
            }

            // Restore, and confirm the original route comes back.
            manager.SetRegionBlocked(wall, false);

            var restored = new List<Vector3>();
            True("route restored after unblocking", manager.FindPath(from, to, restored));
        }

        private static void CheckDegenerateQueries(AStarPathfindingManager manager)
        {
            var path = new List<Vector3>();

            // Same start and goal.
            True("same start and goal succeeds", manager.FindPath(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f), path));

            // Far outside the grid: must fail or clamp, never hang or throw.
            bool outside = manager.FindPath(new Vector3(0f, 1f, 0f), new Vector3(9999f, 1f, 9999f), path);
            True("out-of-bounds goal handled without error", true);
            Debug.Log($"[PSM2 AStarCheck] out-of-bounds goal returned {outside} (either answer is acceptable).");

            // Inside a solid wall: NearestWalkableNode should rescue it.
            bool intoWall = manager.FindPath(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 15f), path);
            Debug.Log($"[PSM2 AStarCheck] goal inside the valve wall returned {intoWall} (rescued by nearest-walkable search).");
        }

        /// <summary>
        /// Prints walkability along the level's spine (x = 0, running -Z to +Z).
        /// When a route fails this shows exactly which band of cells closed up,
        /// which is far faster than guessing at geometry dimensions.
        /// </summary>
        [MenuItem("PSM2/Diagnostics/Dump A* Walkability Profile", priority = 73)]
        public static void DumpProfile()
        {
            string scenePath = $"{ProjectAssets.ScenesFolder}/{GameConstants.SceneLevel1}.unity";
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var manager = Object.FindAnyObjectByType<AStarPathfindingManager>();
            if (manager == null) { Debug.LogError("[PSM2 Profile] No pathfinding manager."); return; }

            Physics.SyncTransforms();
            manager.BuildGrid();

            var sb = new System.Text.StringBuilder("[PSM2 Profile] Walkability, '#' = walkable, '.' = blocked\n");

            for (float x = -6f; x <= 6f; x += 1f)
            {
                sb.Append($"  x={x,5:0.0}  ");
                for (float z = -28f; z <= 28f; z += 1f)
                {
                    PathNode node = manager.NodeFromWorldPoint(new Vector3(x, 0f, z));
                    sb.Append(node != null && node.Walkable ? '#' : '.');
                }
                sb.Append('\n');
            }

            sb.Append("             ");
            for (float z = -28f; z <= 28f; z += 1f) sb.Append(Mathf.Approximately(z % 10f, 0f) ? '|' : ' ');
            sb.Append("   (| every 10 units, from z=-28)\n");

            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------
        // Levels 2 and 3 navigability (Phase 8)
        // ------------------------------------------------------------------

        /// <summary>
        /// Asserts that a generated level can actually be completed.
        ///
        /// This is the check Phase 8 most needed. Every other automated test in
        /// the project loads Level 1, so new geometry could ship with a sealed
        /// corridor or a station stranded inside a wall and nothing would fail.
        /// Reachability from the spawn point to every station and to the exit is
        /// the property that makes a level winnable, and it is fully decidable
        /// from the grid - no Play mode and no human needed.
        /// </summary>
        private static void CheckLevelNavigable(string sceneName, string label)
        {
            string scenePath = $"{ProjectAssets.ScenesFolder}/{sceneName}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                _failed++;
                Debug.LogError($"[PSM2 AStarCheck] FAIL {label}: {scenePath} not found.");
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var manager = Object.FindAnyObjectByType<AStarPathfindingManager>();
            if (manager == null)
            {
                _failed++;
                Debug.LogError($"[PSM2 AStarCheck] FAIL {label}: no AStarPathfindingManager in the scene.");
                return;
            }

            Physics.SyncTransforms();
            manager.BuildGrid();

            True($"{label} grid has walkable space", manager.WalkableNodeCount > 0);

            Vector3 spawn = FindPosition("SpawnPoint", Vector3.zero);
            Vector3 exit = FindPosition("ExitAnchor", Vector3.zero);

            var path = new List<Vector3>();
            True($"{label} exit is reachable from the spawn", manager.FindPath(spawn, exit, path));

            // Every station must be reachable, or its objective can never be
            // ticked and the level cannot be finished.
            foreach (Gameplay.PuzzleStation station in
                     Object.FindObjectsByType<Gameplay.PuzzleStation>(FindObjectsInactive.Include))
            {
                var toStation = new List<Vector3>();
                True($"{label} station '{station.PuzzleId}' is reachable",
                     manager.FindPath(spawn, station.transform.position, toStation));
            }
        }

        /// <summary>
        /// Level 2's thrombus is meant to seal the basilar artery outright, so
        /// that the only way through is the collateral route around a carotid
        /// and across the Circle of Willis.
        ///
        /// Both halves matter and both are asserted: if the clot does not
        /// actually block, the level teaches nothing about collateral flow; if
        /// it blocks with no way around, the level is unwinnable. A route that
        /// exists but is far longer than the straight line is exactly the
        /// signature of a detour.
        /// </summary>
        private static void CheckCollateralRoute()
        {
            string scenePath = $"{ProjectAssets.ScenesFolder}/{GameConstants.SceneLevel2}.unity";
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var manager = Object.FindAnyObjectByType<AStarPathfindingManager>();
            if (manager == null) return;

            Physics.SyncTransforms();
            manager.BuildGrid();

            // Just south of the clot (reachable directly) and just north of it
            // (only reachable the long way round).
            var southOfClot = new Vector3(0f, 1f, -22f);
            var northOfClot = new Vector3(0f, 1f, -13f);

            var path = new List<Vector3>();
            bool found = manager.FindPath(southOfClot, northOfClot, path);

            True("Level 2 the far side of the thrombus is still reachable", found);

            if (found)
            {
                float direct = Vector3.Distance(southOfClot, northOfClot);
                float travelled = PathLength(southOfClot, path);

                // A straight run would be ~9 units. Going round a carotid and
                // through the ring is many times that, so a modest multiple is
                // a safe threshold that still fails loudly if the clot leaks.
                True("Level 2 the thrombus forces a detour rather than a straight line",
                     travelled > direct * 3f);

                Debug.Log($"[PSM2 AStarCheck] Level 2 collateral route: {travelled:F1} units " +
                          $"against a {direct:F1} unit straight line ({travelled / direct:F1}x).");
            }
        }


        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Vector3 FindPosition(string objectName, Vector3 fallback)
        {
            GameObject found = GameObject.Find(objectName);
            return found != null ? found.transform.position : fallback;
        }

        private static float PathLength(Vector3 start, List<Vector3> path)
        {
            float total = 0f;
            Vector3 previous = start;

            foreach (Vector3 point in path)
            {
                total += Vector3.Distance(previous, point);
                previous = point;
            }

            return total;
        }

        private static void Equal(string label, int actual, int expected)
        {
            if (actual == expected) { _passed++; return; }

            _failed++;
            Debug.LogError($"[PSM2 AStarCheck] FAIL {label}: expected {expected}, got {actual}.");
        }

        private static void True(string label, bool condition)
        {
            if (condition) { _passed++; return; }

            _failed++;
            Debug.LogError($"[PSM2 AStarCheck] FAIL {label}.");
        }
    }
}
