namespace Cardio.Core
{
    /// <summary>
    /// Single source of truth for scene names, tags and layer names.
    /// Scenes are referenced by name (not by build index) so that inserting a
    /// scene into Build Settings cannot silently break navigation.
    /// </summary>
    public static class GameConstants
    {
        // ---- Scene names (must match the .unity file names exactly) ----
        public const string SceneMainMenu = "MainMenu";
        public const string SceneLogin = "Login";
        public const string SceneLevel1 = "Level1_LeftVentricle";
        public const string SceneLevel2 = "Level2_Brain";
        public const string SceneLevel3 = "Level3_RightVentricle";

        /// <summary>Level scenes in play order. Index 0 == Level 1.</summary>
        public static readonly string[] LevelScenes =
        {
            SceneLevel1,
            SceneLevel2,
            SceneLevel3
        };

        // ---- Tags ----
        public const string TagPlayer = "Player";
        public const string TagHazard = "Hazard";
        public const string TagInteractable = "Interactable";

        // ---- Layers (created by the editor setup tool) ----
        public const string LayerEnvironment = "Environment";
        public const string LayerObstacle = "Obstacle";
        public const string LayerPlayer = "PlayerLayer";

        // ---- PlayerPrefs keys (options only; gameplay data goes to SaveManager) ----
        public const string PrefMasterVolume = "opt_master_volume";
        public const string PrefMouseSensitivity = "opt_mouse_sensitivity";
        public const string PrefFullscreen = "opt_fullscreen";
        public const string PrefInvertY = "opt_invert_y";

        /// <summary>Maps a level id to its scene name. Returns null for <see cref="LevelId.None"/>.</summary>
        public static string SceneNameFor(LevelId level)
        {
            switch (level)
            {
                case LevelId.Level1_LeftVentricle: return SceneLevel1;
                case LevelId.Level2_Brain: return SceneLevel2;
                case LevelId.Level3_RightVentricle: return SceneLevel3;
                default: return null;
            }
        }

        /// <summary>Human readable level name for the HUD and the objective board.</summary>
        public static string DisplayNameFor(LevelId level)
        {
            switch (level)
            {
                case LevelId.Level1_LeftVentricle: return "Level 1 - Left Ventricle";
                case LevelId.Level2_Brain: return "Level 2 - Brain Circulation";
                case LevelId.Level3_RightVentricle: return "Level 3 - Right Ventricle";
                default: return "-";
            }
        }

        /// <summary>The level that follows <paramref name="level"/>, or None if it was the last one.</summary>
        public static LevelId NextLevel(LevelId level)
        {
            switch (level)
            {
                case LevelId.Level1_LeftVentricle: return LevelId.Level2_Brain;
                case LevelId.Level2_Brain: return LevelId.Level3_RightVentricle;
                default: return LevelId.None;
            }
        }
    }
}
