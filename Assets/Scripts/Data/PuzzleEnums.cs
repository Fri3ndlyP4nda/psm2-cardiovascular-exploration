namespace Cardio.Data
{
    /// <summary>
    /// The five puzzle formats from the PSM1 design.
    ///
    /// The first four are the anatomy-teaching formats and are the ones the
    /// game leans on. MultipleChoice exists for facts that cannot be expressed
    /// spatially (e.g. "which side carries oxygenated blood") and is used
    /// sparingly on purpose - PSM1 explicitly warns against turning the game
    /// into a quiz.
    /// </summary>
    public enum PuzzleType
    {
        /// <summary>Click the correct structure in the 3D world.</summary>
        IdentifyStructure = 0,

        /// <summary>Drag a text label out of the panel and drop it on the correct 3D structure.</summary>
        DragAndDropLabel = 1,

        /// <summary>Put the steps of a circulatory pathway into the correct order.</summary>
        BloodFlowSequence = 2,

        /// <summary>Identify a valve from a description of its function.</summary>
        ValveIdentification = 3,

        /// <summary>Standard single-answer question.</summary>
        MultipleChoice = 4
    }

    /// <summary>
    /// Where a revealed hint came from. The three are scored and reported
    /// differently, so they must stay distinguishable in the evaluation data.
    /// </summary>
    public enum HintSource
    {
        /// <summary>The player asked for it. Carries the HintPenalty.</summary>
        Requested = 0,

        /// <summary>The DDA offered it unprompted because the player was struggling. Free.</summary>
        Automatic = 1,

        /// <summary>
        /// Earned by destroying the leukemic blast spawned from that question.
        /// Not penalised here - the kill itself carries the score cost.
        /// </summary>
        Earned = 2
    }

    /// <summary>How a <see cref="Cardio.Gameplay.LevelObjective"/> is satisfied.</summary>
    public enum ObjectiveKind
    {
        /// <summary>Completed when the puzzle with the matching id is answered correctly.</summary>
        Puzzle = 0,

        /// <summary>Completed when the player enters the level exit trigger.</summary>
        ReachExit = 1,

        /// <summary>Completed only when game code calls CompleteObjective explicitly.</summary>
        Custom = 2
    }

    /// <summary>
    /// Whether a puzzle is answered by pointing at the world or purely inside
    /// the panel. Drives which input mode <c>PuzzleUI</c> switches on.
    /// </summary>
    public static class PuzzleTypeExtensions
    {
        /// <summary>True when answering requires picking a structure in the 3D scene.</summary>
        public static bool UsesWorldPicking(this PuzzleType type)
        {
            return type == PuzzleType.IdentifyStructure
                || type == PuzzleType.DragAndDropLabel
                || type == PuzzleType.ValveIdentification;
        }

        /// <summary>Short human-readable name, shown as the puzzle panel's header.</summary>
        public static string DisplayName(this PuzzleType type)
        {
            switch (type)
            {
                case PuzzleType.IdentifyStructure: return "IDENTIFY THE STRUCTURE";
                case PuzzleType.DragAndDropLabel: return "PLACE THE LABEL";
                case PuzzleType.BloodFlowSequence: return "ORDER THE BLOOD FLOW";
                case PuzzleType.ValveIdentification: return "IDENTIFY THE VALVE";
                default: return "QUESTION";
            }
        }
    }
}
