namespace Cardio.Core
{
    /// <summary>
    /// High level application state. Owned by <see cref="GameManager"/>.
    /// Systems react to state changes instead of polling each other.
    /// </summary>
    public enum GameState
    {
        Boot,
        MainMenu,
        Login,
        Loading,
        Playing,

        /// <summary>
        /// A puzzle panel is open. Distinct from Paused: time keeps running (so
        /// obstacles still move once Phase 5 lands) but the player cannot walk
        /// or turn the camera, and the cursor is released so structures in the
        /// 3D scene can be clicked and labels dragged onto them.
        /// </summary>
        Puzzle,

        Paused,
        LevelComplete,
        GameOver
    }

    /// <summary>
    /// The three difficulty tiers required by the PSM1 design.
    /// The tier is *stored* from Phase 1 onwards so that the HUD and the
    /// session log have somewhere to read from, but nothing changes it
    /// automatically until the DDAManager is implemented in Phase 4.
    /// </summary>
    public enum DifficultyTier
    {
        Easy = 0,
        Medium = 1,
        Hard = 2
    }

    /// <summary>
    /// Stable identifiers for the three playable levels.
    /// Used for save data and session logs so that renaming a scene file
    /// does not invalidate previously stored player progress.
    /// </summary>
    public enum LevelId
    {
        None = 0,
        Level1_LeftVentricle = 1,
        Level2_Brain = 2,
        Level3_RightVentricle = 3
    }
}
