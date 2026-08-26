using Cardio.Core;
using Cardio.DDA;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Seeds the DDA configuration: three <see cref="DifficultySettings"/> tier
    /// assets held as sub-assets of one <see cref="DDAConfig"/>.
    ///
    /// The config lives in Resources because DDAManager is created at runtime by
    /// GameBootstrap and so has no Inspector to be wired through.
    ///
    /// Like the question banks and unlike the scenes, this is **non-destructive**:
    /// difficulty values are exactly what gets retuned during playtesting, and
    /// a rebuild silently reverting that tuning would be infuriating. Use
    /// PSM2 > Content > Reseed Difficulty Config (Destructive) to force it.
    ///
    /// Starting values follow the PSM1 section 26 table.
    /// </summary>
    public static class DifficultyFactory
    {
        public const string ResourcesFolder = "Assets/Resources";
        public const string DDAFolder = "Assets/Resources/DDA";
        public const string ConfigPath = "Assets/Resources/DDA/DDAConfig.asset";

        /// <summary>Creates the config if missing. Returns it either way.</summary>
        public static DDAConfig CreateConfig(bool forceReseed)
        {
            EnsureFolders();

            var existing = AssetDatabase.LoadAssetAtPath<DDAConfig>(ConfigPath);
            if (existing != null && !forceReseed)
            {
                Debug.Log($"[PSM2] DDA config already exists ({(existing.IsComplete ? "complete" : "INCOMPLETE")}) - left untouched.");
                return existing;
            }

            if (existing != null) AssetDatabase.DeleteAsset(ConfigPath);

            var config = ScriptableObject.CreateInstance<DDAConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);

            config.Easy = AddTier(config, BuildEasy());
            config.Medium = AddTier(config, BuildMedium());
            config.Hard = AddTier(config, BuildHard());

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log("[PSM2] Seeded DDA config with Easy / Medium / Hard tiers.");
            return config;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(DDAFolder)) AssetDatabase.CreateFolder(ResourcesFolder, "DDA");
        }

        private static DifficultySettings AddTier(DDAConfig config, DifficultySettings tier)
        {
            tier.name = $"Difficulty_{tier.Tier}";
            AssetDatabase.AddObjectToAsset(tier, config);
            return tier;
        }

        // ------------------------------------------------------------------
        // Tier definitions (PSM1 section 26 starting values)
        // ------------------------------------------------------------------

        /// <summary>
        /// Easy: only the simplest questions, generous attempts and time,
        /// hints offered quickly, environment softened.
        /// </summary>
        private static DifficultySettings BuildEasy()
        {
            var s = ScriptableObject.CreateInstance<DifficultySettings>();
            s.Tier = DifficultyTier.Easy;
            s.MaxPuzzleComplexity = 1;
            s.MaxPuzzleAttempts = 5;
            s.ResponseTimeAllowance = 1.5f;
            s.ObstacleSpeedMultiplier = 0.5f;
            s.HazardDamageMultiplier = 0.6f;
            s.HintFrequency = HintFrequency.High;
            s.AutoHintDelaySeconds = 12f;
            s.AutoHintAfterFailedAttempts = 1;
            s.HighlightStructureOnHint = true;
            return s;
        }

        /// <summary>Medium: the reference tier. Every multiplier is 1.0.</summary>
        private static DifficultySettings BuildMedium()
        {
            var s = ScriptableObject.CreateInstance<DifficultySettings>();
            s.Tier = DifficultyTier.Medium;
            s.MaxPuzzleComplexity = 2;
            s.MaxPuzzleAttempts = 3;
            s.ResponseTimeAllowance = 1f;
            s.ObstacleSpeedMultiplier = 1f;
            s.HazardDamageMultiplier = 1f;
            s.HintFrequency = HintFrequency.Medium;
            s.AutoHintDelaySeconds = 25f;
            s.AutoHintAfterFailedAttempts = 2;
            s.HighlightStructureOnHint = true;
            return s;
        }

        /// <summary>
        /// Hard: the full question bank, fewer attempts, a tighter clock and no
        /// unprompted help. Manual hints remain available - taking difficulty
        /// away is the DDA's job, but hiding a learning aid entirely would work
        /// against the educational purpose.
        /// </summary>
        private static DifficultySettings BuildHard()
        {
            var s = ScriptableObject.CreateInstance<DifficultySettings>();
            s.Tier = DifficultyTier.Hard;
            s.MaxPuzzleComplexity = 3;
            s.MaxPuzzleAttempts = 2;
            s.ResponseTimeAllowance = 0.8f;
            s.ObstacleSpeedMultiplier = 1.5f;
            s.HazardDamageMultiplier = 1.4f;
            s.HintFrequency = HintFrequency.Low;
            s.AutoHintDelaySeconds = 0f;              // never offered unprompted
            s.AutoHintAfterFailedAttempts = 0;
            s.HighlightStructureOnHint = false;
            return s;
        }
    }
}
