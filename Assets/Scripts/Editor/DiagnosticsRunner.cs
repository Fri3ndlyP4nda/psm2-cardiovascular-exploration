using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Runs every headless self-check in one pass.
    ///
    /// Useful as a regression gate: one menu item (or one -executeMethod) covers
    /// the scoring arithmetic, the difficulty policy and the pathfinding grid.
    /// The PlayMode integration tests are separate because they need the test
    /// runner - see Documentation/TESTING.md for the command.
    /// </summary>
    public static class DiagnosticsRunner
    {
        [MenuItem("PSM2/Diagnostics/Run All Self-Checks", priority = 69)]
        public static void RunAll()
        {
            Debug.Log("[PSM2] ===== Running all self-checks =====");

            PerformanceSelfCheck.Run();
            DDASelfCheck.Run();

            // These two open scenes, so they run last.
            AStarSelfCheck.Run();
            PerformanceBudgetCheck.Run();

            Debug.Log("[PSM2] ===== Self-checks complete =====");
        }
    }
}
