using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Captures the Game View to a PNG, for UI/UX review.
    ///
    /// WHY THIS EXISTS RATHER THAN AN MCP TOOL. The Unity MCP servers that attach
    /// to a running Editor (Unity's own com.unity.ai.assistant, and the
    /// Coplay/snowfox bridge) expose scene, script and console tools but **no image
    /// capture at all**. The one server that does expose a screenshot tool
    /// (nurture-tech) launches its *own* Unity process - which cannot work while you
    /// have the project open, because Unity holds an exclusive lock on a project
    /// directory. So no MCP route currently produces a picture of this game's UI.
    ///
    /// This does, with no dependencies: press the menu item (or Ctrl+Shift+U) while
    /// in Play mode and it writes a timestamped PNG under uishots/ in the project
    /// root, which an agent can then read directly off disk.
    ///
    /// Batch mode is deliberately not supported. ScreenCapture needs a real frame,
    /// and the coroutine idiom for waiting on one - WaitForEndOfFrame - never
    /// resumes under -batchmode, so a headless capture hangs forever instead of
    /// failing. That was established the hard way.
    /// </summary>
    public static class UiScreenshot
    {
        private const string Folder = "uishots";

        [MenuItem("PSM2/Diagnostics/Capture Game View %#u", priority = 75)]
        public static void Capture()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Not in Play mode",
                    "Enter Play mode first. The Game View only renders the UI while the game is running, " +
                    "so a capture taken in Edit mode would be empty.",
                    "OK");
                return;
            }

            Directory.CreateDirectory(Folder);

            // Sortable, and unique enough that repeated captures never collide.
            string stamp = System.DateTime.Now.ToString("HHmmss");
            string path = Path.Combine(Folder, $"ui_{stamp}.png");

            // Writes on the next rendered frame; the file will not exist yet when
            // this returns, which is why the log line is the confirmation and not a
            // File.Exists check here.
            ScreenCapture.CaptureScreenshot(path);

            Debug.Log($"[PSM2] Capturing Game View -> {Path.GetFullPath(path)} " +
                      $"({Screen.width}x{Screen.height}). Appears within a frame or two.");
        }

        [MenuItem("PSM2/Diagnostics/Capture Game View %#u", validate = true)]
        private static bool CaptureValidate() => Application.isPlaying;

        /// <summary>Opens the folder so the shots are easy to find and hand over.</summary>
        [MenuItem("PSM2/Diagnostics/Open Screenshot Folder", priority = 76)]
        public static void OpenFolder()
        {
            Directory.CreateDirectory(Folder);
            EditorUtility.RevealInFinder(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar);
        }
    }
}
