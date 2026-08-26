using System.IO;
using Cardio.Core;
using Cardio.Data;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Aliased because the obsolete UnityEditor.PackageInfo type would otherwise
// make the unqualified name ambiguous against `using UnityEditor;`.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Cardio.EditorTools
{
    /// <summary>
    /// The one-click project generator.
    ///
    /// Menu:  PSM2 > Setup > Build or Rebuild Project
    ///
    /// It creates the folder structure, tags, layers, materials, the player
    /// prefab and all five scenes, then registers those scenes in Build
    /// Settings and applies the player/quality settings the PSM1 performance
    /// target implies.
    ///
    /// Running it is safe and repeatable: every step overwrites its own output
    /// and nothing else in the project is touched.
    /// </summary>
    public static class PSM2SetupTool
    {
        private const string MenuRoot = "PSM2/";

        /// <summary>The package that ships both UnityEngine.UI and TextMeshPro on Unity 6.</summary>
        private const string UguiPackageName = "com.unity.ugui";

        [MenuItem(MenuRoot + "Setup/Build or Rebuild Project", priority = 0)]
        public static void BuildProject()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[PSM2] Setup cancelled - the open scene has unsaved changes.");
                return;
            }

            // EditorUtility.DisplayDialog cannot be used in batch mode: Unity logs
            // "This should not be called in batch mode" and returns false, which
            // would silently cancel every headless or CI run. Interactive runs
            // still get the confirmation prompt.
            if (!Application.isBatchMode)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Rebuild PSM2 project",
                    "This regenerates:\n\n" +
                    "  - the Assets folder structure\n" +
                    "  - tags, layers and shared materials\n" +
                    "  - the Bloo.D. Clot player prefab\n" +
                    "  - all 5 scenes (MainMenu, Login, Level 1-3)\n" +
                    "  - Build Settings scene list\n\n" +
                    "Any manual edits to those generated scenes will be REPLACED.\n\nContinue?",
                    "Rebuild", "Cancel");

                if (!proceed) return;
            }
            else
            {
                Debug.Log("[PSM2] Batch mode detected - skipping the confirmation prompt.");
            }

            if (!PreflightTextMeshPro()) return;

            try
            {
                EditorUtility.DisplayProgressBar("PSM2 Setup", "Creating folders...", 0.05f);
                ProjectAssets.CreateFolders();

                EditorUtility.DisplayProgressBar("PSM2 Setup", "Creating tags and layers...", 0.15f);
                ProjectAssets.CreateTagsAndLayers();
                AssetDatabase.SaveAssets();

                EditorUtility.DisplayProgressBar("PSM2 Setup", "Creating materials...", 0.3f);
                CreateAllMaterials();

                EditorUtility.DisplayProgressBar("PSM2 Setup", "Building prefabs...", 0.4f);
                GameObject playerPrefab = PrefabFactory.CreatePlayerPrefab();
                (GameObject neutrophil, GameObject monocyte) = PrefabFactory.CreateObstaclePrefabs();
                GameObject blast = PrefabFactory.CreateBlastPrefab();

                // Banks must exist before the scenes, because each level's
                // PuzzleManager is wired to its bank asset during generation.
                // Existing banks are preserved - see QuestionBankFactory.
                EditorUtility.DisplayProgressBar("PSM2 Setup", "Seeding question banks...", 0.5f);
                QuestionBankFactory.CreateBanks(forceReseed: false);

                // DDAManager loads this from Resources at runtime, so it must
                // exist before the game is played. Also non-destructive.
                EditorUtility.DisplayProgressBar("PSM2 Setup", "Seeding difficulty config...", 0.55f);
                DifficultyFactory.CreateConfig(forceReseed: false);

                EditorUtility.DisplayProgressBar("PSM2 Setup", "Generating scenes...", 0.6f);
                SceneFactory.GenerateAllScenes(playerPrefab, neutrophil, monocyte, blast);

                EditorUtility.DisplayProgressBar("PSM2 Setup", "Applying project settings...", 0.9f);
                ApplyProjectSettings();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorSceneManager.OpenScene($"{ProjectAssets.ScenesFolder}/{GameConstants.SceneMainMenu}.unity");

            Debug.Log("[PSM2] Project generated. Press Play in the MainMenu scene to test, " +
                      "or open Assets/Scenes/Level1_LeftVentricle.unity and press Play to go straight to gameplay.");
        }

        [MenuItem(MenuRoot + "Setup/Create Folder Structure Only", priority = 20)]
        public static void CreateFoldersOnly()
        {
            ProjectAssets.CreateFolders();
            ProjectAssets.CreateTagsAndLayers();
            AssetDatabase.SaveAssets();
            Debug.Log("[PSM2] Folder structure, tags and layers created.");
        }

        [MenuItem(MenuRoot + "Setup/Register Scenes in Build Settings", priority = 21)]
        public static void RegisterScenes()
        {
            SceneFactory.RegisterScenesInBuildSettings();
            Debug.Log("[PSM2] Build Settings scene list updated.");
        }

        [MenuItem(MenuRoot + "Setup/Apply Player and Quality Settings", priority = 22)]
        public static void ApplyProjectSettings()
        {
            PlayerSettings.companyName = "PSM2 FYP";
            PlayerSettings.productName = "Cardiovascular Exploration";
            PlayerSettings.bundleVersion = "0.1.0";

            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            // Performance: the PSM1 target is 60 FPS on a standard laptop.
            // VSync is left on so the frame rate is capped at the display rate
            // instead of burning GPU time, and shadow distance is short because
            // the generated environments do not use real-time shadows at all.
            QualitySettings.vSyncCount = 1;
            QualitySettings.shadowDistance = 40f;
            QualitySettings.shadows = ShadowQuality.HardOnly;
            QualitySettings.antiAliasing = 2;

            Debug.Log("[PSM2] Player and quality settings applied.");
        }

        // ------------------------------------------------------------------
        // Content
        // ------------------------------------------------------------------

        [MenuItem(MenuRoot + "Content/Create Missing Question Banks", priority = 30)]
        public static void CreateQuestionBanks()
        {
            QuestionBankFactory.CreateBanks(forceReseed: false);
            AssetDatabase.SaveAssets();
        }

        [MenuItem(MenuRoot + "Content/Create Missing Difficulty Config", priority = 33)]
        public static void CreateDifficultyConfig()
        {
            DifficultyFactory.CreateConfig(forceReseed: false);
            AssetDatabase.SaveAssets();
        }

        [MenuItem(MenuRoot + "Content/Reseed Difficulty Config (Destructive)", priority = 34)]
        public static void ReseedDifficultyConfig()
        {
            bool proceed = Application.isBatchMode || EditorUtility.DisplayDialog(
                "Reseed difficulty config",
                "This DELETES Assets/Resources/DDA/DDAConfig.asset and rebuilds it from " +
                "DifficultyFactory.\n\nAny difficulty tuning you have done will be lost.\n\nContinue?",
                "Reseed", "Cancel");

            if (!proceed) return;

            DifficultyFactory.CreateConfig(forceReseed: true);
            AssetDatabase.SaveAssets();
            Debug.Log("[PSM2] Difficulty config reseeded.");
        }

        [MenuItem(MenuRoot + "Content/Reseed Question Banks (Destructive)", priority = 31)]
        public static void ReseedQuestionBanks()
        {
            bool proceed = Application.isBatchMode || EditorUtility.DisplayDialog(
                "Reseed question banks",
                "This DELETES the three question bank assets in Assets/Data and rebuilds them " +
                "from QuestionBankFactory.\n\nAny wording you have edited in the Inspector will be lost.\n\nContinue?",
                "Reseed", "Cancel");

            if (!proceed) return;

            QuestionBankFactory.CreateBanks(forceReseed: true);
            AssetDatabase.SaveAssets();
            Debug.Log("[PSM2] Question banks reseeded.");
        }

        /// <summary>
        /// Checks every bank for authoring errors, and confirms that each
        /// structure puzzle points at a structure that actually exists in its
        /// level scene. A typo'd TargetStructureId produces a puzzle that can
        /// never be answered, which is otherwise only discovered by playing.
        /// </summary>
        [MenuItem(MenuRoot + "Content/Validate Question Banks", priority = 32)]
        public static void ValidateQuestionBanks()
        {
            var problems = new System.Collections.Generic.List<string>();

            foreach (LevelId level in new[] { LevelId.Level1_LeftVentricle, LevelId.Level2_Brain, LevelId.Level3_RightVentricle })
            {
                string path = QuestionBankFactory.BankPath(level);
                var bank = AssetDatabase.LoadAssetAtPath<QuestionBank>(path);

                if (bank == null)
                {
                    problems.Add($"{level}: no question bank at {path}.");
                    continue;
                }

                problems.AddRange(bank.Validate());
                problems.AddRange(ValidateStructureTargets(bank, level));
            }

            if (problems.Count == 0)
            {
                Debug.Log("[PSM2] Question banks validated - no problems found.");
                return;
            }

            foreach (string problem in problems) Debug.LogError($"[PSM2] {problem}");
            Debug.LogError($"[PSM2] Validation finished with {problems.Count} problem(s).");
        }

        /// <summary>Confirms structure puzzles reference ids present in the level scene.</summary>
        private static System.Collections.Generic.List<string> ValidateStructureTargets(QuestionBank bank, LevelId level)
        {
            var problems = new System.Collections.Generic.List<string>();

            string scenePath = $"{ProjectAssets.ScenesFolder}/{GameConstants.SceneNameFor(level)}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) return problems;

            // Reading the scene YAML avoids having to open it, which would
            // discard whatever the user currently has loaded.
            string yaml;
            try { yaml = System.IO.File.ReadAllText(scenePath); }
            catch (System.Exception e) { problems.Add($"{level}: could not read {scenePath} ({e.Message})."); return problems; }

            foreach (PuzzleData puzzle in bank.Puzzles)
            {
                if (puzzle == null || !puzzle.Type.UsesWorldPicking()) continue;
                if (string.IsNullOrWhiteSpace(puzzle.TargetStructureId)) continue;

                if (!yaml.Contains(puzzle.TargetStructureId))
                {
                    problems.Add($"{level}: puzzle '{puzzle.PuzzleId}' targets structure " +
                                 $"'{puzzle.TargetStructureId}', which does not appear in {GameConstants.SceneNameFor(level)}.");
                }
            }

            return problems;
        }

        // ------------------------------------------------------------------
        // Scene shortcuts
        // ------------------------------------------------------------------

        [MenuItem(MenuRoot + "Open Scene/Main Menu", priority = 40)]
        private static void OpenMainMenu() => OpenScene(GameConstants.SceneMainMenu);

        [MenuItem(MenuRoot + "Open Scene/Login", priority = 41)]
        private static void OpenLogin() => OpenScene(GameConstants.SceneLogin);

        [MenuItem(MenuRoot + "Open Scene/Level 1 - Left Ventricle", priority = 42)]
        private static void OpenLevel1() => OpenScene(GameConstants.SceneLevel1);

        [MenuItem(MenuRoot + "Open Scene/Level 2 - Brain", priority = 43)]
        private static void OpenLevel2() => OpenScene(GameConstants.SceneLevel2);

        [MenuItem(MenuRoot + "Open Scene/Level 3 - Right Ventricle", priority = 44)]
        private static void OpenLevel3() => OpenScene(GameConstants.SceneLevel3);

        [MenuItem(MenuRoot + "Open Save File Folder", priority = 60)]
        private static void OpenSaveFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);

        private static void OpenScene(string sceneName)
        {
            string path = $"{ProjectAssets.ScenesFolder}/{sceneName}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                EditorUtility.DisplayDialog("Scene not found",
                    $"{path} does not exist yet.\n\nRun PSM2 > Setup > Build or Rebuild Project first.", "OK");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// TextMeshPro needs its Essential Resources imported before any TMP
        /// text can render. Generating the UI without them produces a scene
        /// full of invisible labels, so the tool imports them first.
        ///
        /// The resources ship as a .unitypackage inside com.unity.ugui. Its
        /// location on disk contains a content hash that changes between
        /// package versions and machines, so the path is resolved through the
        /// Package Manager rather than hard-coded.
        /// </summary>
        private static bool PreflightTextMeshPro()
        {
            if (TmpSettingsPresent()) return true;

            string packagePath = ResolveTmpEssentialsPackage();
            if (string.IsNullOrEmpty(packagePath))
            {
                Debug.LogError(
                    "[PSM2] Could not locate 'TMP Essential Resources.unitypackage' inside " +
                    $"{UguiPackageName}. Import it manually with " +
                    "Window > TextMeshPro > Import TMP Essential Resources, then run this tool again.");
                return false;
            }

            Debug.Log($"[PSM2] Importing TMP Essential Resources from: {packagePath}");

            // interactive:false - the import-confirmation window would block a
            // batch-mode run exactly the way the rebuild dialog used to.
            AssetDatabase.ImportPackage(packagePath, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            if (TmpSettingsPresent())
            {
                Debug.Log("[PSM2] TMP Essential Resources imported.");
                return true;
            }

            // ImportPackage is queued rather than immediate, so on a cold project
            // the assets may only become loadable after this run finishes.
            Debug.LogWarning(
                "[PSM2] TMP Essential Resources were imported but are not loadable yet in this pass. " +
                "Run PSM2 > Setup > Build or Rebuild Project once more to finish generating.");
            return false;
        }

        /// <summary>True once TMP's settings asset is present and loadable.</summary>
        private static bool TmpSettingsPresent()
        {
            // Loaded directly rather than through TMP_Settings.instance, whose
            // getter logs its own error when the asset is absent.
            return Resources.Load<TMP_Settings>("TMP Settings") != null;
        }

        /// <summary>
        /// Finds the TMP Essential Resources package on disk by asking the
        /// Package Manager where com.unity.ugui resolved to.
        /// </summary>
        private static string ResolveTmpEssentialsPackage()
        {
            const string relativePath = "Package Resources/TMP Essential Resources.unitypackage";

            // Preferred: look the package up by name.
            PackageInfo package = PackageInfo.FindForPackageName(UguiPackageName);

            // Fallback: derive it from an assembly known to ship inside the package.
            // Covers the case where the package is embedded or locally sourced
            // under a different name.
            if (package == null) package = PackageInfo.FindForAssembly(typeof(TMP_Settings).Assembly);

            if (package == null)
            {
                Debug.LogError($"[PSM2] Package Manager could not resolve {UguiPackageName}.");
                return null;
            }

            string candidate = Path.Combine(package.resolvedPath, relativePath).Replace('\\', '/');
            return File.Exists(candidate) ? candidate : null;
        }

        /// <summary>Touches every shared material so they all exist before the scenes reference them.</summary>
        private static void CreateAllMaterials()
        {
            _ = ProjectAssets.MuscleWall;
            _ = ProjectAssets.MuscleWallDark;
            _ = ProjectAssets.Endocardium;
            _ = ProjectAssets.ValveTissue;
            _ = ProjectAssets.Plaque;
            _ = ProjectAssets.BloodCell;
            _ = ProjectAssets.CellHighlight;
            _ = ProjectAssets.EyeWhite;
            _ = ProjectAssets.EyePupil;
            _ = ProjectAssets.Oxygenated;
            _ = ProjectAssets.Deoxygenated;
            _ = ProjectAssets.ExitGlow;
            _ = ProjectAssets.StationPending;
            _ = ProjectAssets.StationSolved;
            _ = ProjectAssets.Neutrophil;
            _ = ProjectAssets.NeutrophilNucleus;
            _ = ProjectAssets.Monocyte;
            _ = ProjectAssets.MonocyteNucleus;
            _ = ProjectAssets.BlastBody;
            _ = ProjectAssets.BlastNucleus;

            AssetDatabase.SaveAssets();
        }
    }
}
