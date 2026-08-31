using System.Collections.Generic;
using Cardio.AI;
using Cardio.Core;
using Cardio.Data;
using Cardio.Gameplay;
using Cardio.Player;
using Cardio.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Generates every scene in the project and registers them in Build Settings.
    ///
    /// Each scene is created empty and populated from code, so re-running the
    /// tool always yields the same scene - there is no half-edited state to
    /// reason about, which matters when the same project is opened on a
    /// different machine for the demonstration.
    /// </summary>
    public static class SceneFactory
    {
        private static readonly Color MenuBackground = new Color(0.06f, 0.03f, 0.05f);
        private static readonly Color LevelBackground = new Color(0.10f, 0.02f, 0.04f);

        /// <summary>A puzzle station to place in the world.</summary>
        private struct StationSpec
        {
            public string PuzzleId;
            public string Prompt;
            public Vector3 Position;

            public StationSpec(string puzzleId, string prompt, Vector3 position)
            {
                PuzzleId = puzzleId;
                Prompt = prompt;
                Position = position;
            }
        }

        /// <summary>One mobile obstacle to place, with optional patrol route.</summary>
        private struct ObstacleSpec
        {
            public bool IsMonocyte;
            public Vector3 Position;
            public Vector3[] PatrolPoints;

            public static ObstacleSpec Neutrophil(Vector3 position)
                => new ObstacleSpec { IsMonocyte = false, Position = position, PatrolPoints = null };

            public static ObstacleSpec Monocyte(Vector3 position, params Vector3[] patrol)
                => new ObstacleSpec { IsMonocyte = true, Position = position, PatrolPoints = patrol };
        }

        /// <summary>A row on the objective clipboard.</summary>
        private struct ObjectiveSpec
        {
            public string Description;
            public ObjectiveKind Kind;
            public string PuzzleId;

            public static ObjectiveSpec Puzzle(string description, string puzzleId)
                => new ObjectiveSpec { Description = description, Kind = ObjectiveKind.Puzzle, PuzzleId = puzzleId };

            public static ObjectiveSpec Exit(string description)
                => new ObjectiveSpec { Description = description, Kind = ObjectiveKind.ReachExit, PuzzleId = string.Empty };
        }

        // ------------------------------------------------------------------
        // Entry point
        // ------------------------------------------------------------------

        private static GameObject _neutrophilPrefab;
        private static GameObject _monocytePrefab;
        private static GameObject _blastPrefab;

        public static void GenerateAllScenes(GameObject playerPrefab, GameObject neutrophilPrefab,
                                             GameObject monocytePrefab, GameObject blastPrefab)
        {
            _neutrophilPrefab = neutrophilPrefab;
            _monocytePrefab = monocytePrefab;
            _blastPrefab = blastPrefab;
            UIFactory.ResetCache();

            CreateMainMenuScene();
            CreateLoginScene();

            CreateLevel1Scene(playerPrefab);
            CreateLevel2Scene(playerPrefab);
            CreateLevel3Scene(playerPrefab);

            RegisterScenesInBuildSettings();
        }

        private static string ScenePath(string sceneName) => $"{ProjectAssets.ScenesFolder}/{sceneName}.unity";

        private static Scene NewScene()
        {
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void SaveScene(Scene scene, string sceneName)
        {
            EditorSceneManager.SaveScene(scene, ScenePath(sceneName));
        }

        /// <summary>Adds every scene to Build Settings in play order (menu first).</summary>
        public static void RegisterScenesInBuildSettings()
        {
            var order = new List<string>
            {
                GameConstants.SceneMainMenu,
                GameConstants.SceneLogin,
                GameConstants.SceneLevel1,
                GameConstants.SceneLevel2,
                GameConstants.SceneLevel3
            };

            var scenes = new List<EditorBuildSettingsScene>();
            foreach (string name in order)
            {
                string path = ScenePath(name);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    Debug.LogWarning($"[SceneFactory] Scene asset missing, not registered: {path}");
                    continue;
                }
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ------------------------------------------------------------------
        // Main menu
        // ------------------------------------------------------------------

        private static void CreateMainMenuScene()
        {
            Scene scene = NewScene();

            CreateUiCamera(MenuBackground);
            UIFactory.CreateEventSystem();

            Canvas canvas = UIFactory.CreateCanvas("UI_MainMenu", 0);
            var menu = canvas.gameObject.AddComponent<MainMenuUI>();

            UIFactory.CreatePanel(canvas.transform, "Backdrop", new Color(0.06f, 0.03f, 0.05f, 1f));

            // ---- Title block ----
            RectTransform header = UIFactory.CreateRect(canvas.transform, "Header");
            UIFactory.SetRect(header, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1300f, 220f));

            TMP_Text title = UIFactory.CreateText(header, "Title", "CARDIOVASCULAR EXPLORATION",
                72f, TextAlignmentOptions.Center, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1300f, 90f));

            TMP_Text subtitle = UIFactory.CreateText(header, "Subtitle", "using Adaptive Gameplay Mechanics",
                30f, TextAlignmentOptions.Center, UIFactory.ColorAccent, FontStyles.Italic);
            UIFactory.SetRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -125f), new Vector2(1300f, 44f));

            // ---- Root button column ----
            RectTransform rootPanel = UIFactory.CreateRect(canvas.transform, "RootPanel");
            UIFactory.SetRect(rootPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(460f, 440f));
            UIFactory.AddVerticalLayout(rootPanel.gameObject, 14f);

            Button startButton = UIFactory.CreateButton(rootPanel, "Btn_Start", "START GAME", new Vector2(460f, 66f));
            Button continueButton = UIFactory.CreateButton(rootPanel, "Btn_Continue", "CONTINUE", new Vector2(460f, 66f));
            Button profileButton = UIFactory.CreateButton(rootPanel, "Btn_Profile", "PROFILE", new Vector2(460f, 66f));
            Button settingsButton = UIFactory.CreateButton(rootPanel, "Btn_Settings", "SETTINGS", new Vector2(460f, 66f));
            Button exitButton = UIFactory.CreateButton(rootPanel, "Btn_Exit", "EXIT", new Vector2(460f, 66f));

            AddLayoutHeight(startButton.gameObject, 66f);
            AddLayoutHeight(continueButton.gameObject, 66f);
            AddLayoutHeight(profileButton.gameObject, 66f);
            AddLayoutHeight(settingsButton.gameObject, 66f);
            AddLayoutHeight(exitButton.gameObject, 66f);

            // ---- Level select ----
            RectTransform levelPanel = UIFactory.CreateRect(canvas.transform, "LevelSelectPanel");
            UIFactory.SetRect(levelPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(720f, 460f));

            Image levelBg = UIFactory.CreatePanel(levelPanel, "Background", UIFactory.ColorPanel);
            levelBg.transform.SetAsFirstSibling();

            TMP_Text levelTitle = UIFactory.CreateText(levelPanel, "Title", "SELECT A LEVEL", 34f,
                TextAlignmentOptions.Center, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.SetRect(levelTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(680f, 48f));

            RectTransform levelColumn = UIFactory.CreateRect(levelPanel, "Buttons");
            UIFactory.SetRect(levelColumn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(600f, 320f));
            UIFactory.AddVerticalLayout(levelColumn.gameObject, 12f);

            Button level1 = UIFactory.CreateButton(levelColumn, "Btn_Level1", "1 - LEFT VENTRICLE", new Vector2(600f, 62f), 24f);
            Button level2 = UIFactory.CreateButton(levelColumn, "Btn_Level2", "2 - BRAIN CIRCULATION", new Vector2(600f, 62f), 24f);
            Button level3 = UIFactory.CreateButton(levelColumn, "Btn_Level3", "3 - RIGHT VENTRICLE", new Vector2(600f, 62f), 24f);
            Button levelBack = UIFactory.CreateButton(levelColumn, "Btn_Back", "BACK", new Vector2(600f, 54f), 22f);

            AddLayoutHeight(level1.gameObject, 62f);
            AddLayoutHeight(level2.gameObject, 62f);
            AddLayoutHeight(level3.gameObject, 62f);
            AddLayoutHeight(levelBack.gameObject, 54f);

            levelPanel.gameObject.SetActive(false);

            // ---- Settings ----
            SettingsPanel settings = BuildSettingsPanel(canvas.transform);
            settings.gameObject.SetActive(false);

            // ---- Footer ----
            TMP_Text signedIn = UIFactory.CreateText(canvas.transform, "SignedInLabel", "Signed in as: Guest",
                22f, TextAlignmentOptions.TopLeft, UIFactory.ColorTextDim);
            UIFactory.SetRect(signedIn.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(230f, 60f), new Vector2(440f, 34f));

            TMP_Text notice = UIFactory.CreateText(canvas.transform, "NoticeLabel", string.Empty,
                22f, TextAlignmentOptions.Center, UIFactory.ColorAccent);
            UIFactory.SetRect(notice.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1100f, 34f));

            TMP_Text version = UIFactory.CreateText(canvas.transform, "VersionLabel", "v0.1.0",
                20f, TextAlignmentOptions.BottomRight, UIFactory.ColorTextDim);
            UIFactory.SetRect(version.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-450f, 40f), new Vector2(880f, 30f));

            // ---- Wire ----
            using (var w = new EditorWiring(menu))
            {
                w.Set("startButton", startButton);
                w.Set("continueButton", continueButton);
                w.Set("profileButton", profileButton);
                w.Set("settingsButton", settingsButton);
                w.Set("exitButton", exitButton);
                w.Set("rootPanel", rootPanel.gameObject);
                w.Set("levelSelectPanel", levelPanel.gameObject);
                w.Set("settingsPanel", settings);
                w.Set("level1Button", level1);
                w.Set("level2Button", level2);
                w.Set("level3Button", level3);
                w.Set("levelSelectBackButton", levelBack);
                w.Set("signedInAsLabel", signedIn);
                w.Set("versionLabel", version);
                w.Set("noticeLabel", notice);
            }

            SaveScene(scene, GameConstants.SceneMainMenu);
        }

        // ------------------------------------------------------------------
        // Login
        // ------------------------------------------------------------------

        private static void CreateLoginScene()
        {
            Scene scene = NewScene();

            CreateUiCamera(MenuBackground);
            UIFactory.CreateEventSystem();

            Canvas canvas = UIFactory.CreateCanvas("UI_Login", 0);
            var login = canvas.gameObject.AddComponent<LoginUI>();

            UIFactory.CreatePanel(canvas.transform, "Backdrop", new Color(0.06f, 0.03f, 0.05f, 1f));

            RectTransform panel = UIFactory.CreateRect(canvas.transform, "LoginPanel");
            UIFactory.SetRect(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 640f));
            UIFactory.CreatePanel(panel, "Background", UIFactory.ColorPanel).transform.SetAsFirstSibling();

            TMP_Text title = UIFactory.CreateText(panel, "Title", "SIGN IN", 44f, TextAlignmentOptions.Center, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(700f, 56f));

            RectTransform column = UIFactory.CreateRect(panel, "Fields");
            UIFactory.SetRect(column, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(620f, 400f));
            UIFactory.AddVerticalLayout(column.gameObject, 12f);

            TMP_InputField email = UIFactory.CreateInputField(column, "Field_Email", "Email address", new Vector2(620f, 56f), TMP_InputField.ContentType.EmailAddress);
            TMP_InputField password = UIFactory.CreateInputField(column, "Field_Password", "Password", new Vector2(620f, 56f), TMP_InputField.ContentType.Password);
            TMP_InputField displayName = UIFactory.CreateInputField(column, "Field_DisplayName", "Display name (guest play)", new Vector2(620f, 56f));

            Button loginButton = UIFactory.CreateButton(column, "Btn_Login", "LOGIN", new Vector2(620f, 58f), 24f);
            Button registerButton = UIFactory.CreateButton(column, "Btn_Register", "REGISTER", new Vector2(620f, 58f), 24f);
            Button guestButton = UIFactory.CreateButton(column, "Btn_Guest", "CONTINUE AS GUEST", new Vector2(620f, 58f), 24f);
            Button backButton = UIFactory.CreateButton(column, "Btn_Back", "BACK", new Vector2(620f, 50f), 22f);

            AddLayoutHeight(email.gameObject, 56f);
            AddLayoutHeight(password.gameObject, 56f);
            AddLayoutHeight(displayName.gameObject, 56f);
            AddLayoutHeight(loginButton.gameObject, 58f);
            AddLayoutHeight(registerButton.gameObject, 58f);
            AddLayoutHeight(guestButton.gameObject, 58f);
            AddLayoutHeight(backButton.gameObject, 50f);

            TMP_Text status = UIFactory.CreateText(panel, "StatusLabel", string.Empty, 20f,
                TextAlignmentOptions.Center, UIFactory.ColorAccent);
            UIFactory.SetRect(status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(680f, 72f));

            using (var w = new EditorWiring(login))
            {
                w.Set("emailField", email);
                w.Set("passwordField", password);
                w.Set("displayNameField", displayName);
                w.Set("loginButton", loginButton);
                w.Set("registerButton", registerButton);
                w.Set("guestButton", guestButton);
                w.Set("backButton", backButton);
                w.Set("statusLabel", status);
            }

            SaveScene(scene, GameConstants.SceneLogin);
        }

        // ------------------------------------------------------------------
        // Levels
        // ------------------------------------------------------------------

        private static void CreateLevel1Scene(GameObject playerPrefab)
        {
            Scene scene = NewScene();

            EnvironmentFactory.SetupLighting(
                ambient: new Color(0.34f, 0.20f, 0.22f),
                fogColor: new Color(0.14f, 0.03f, 0.05f),
                fogDensity: 0.014f);

            LevelEnvironment env = EnvironmentFactory.BuildLeftVentricle();

            // Six stations, one per PSM1 puzzle format, each standing next to the
            // structure it asks about so the answer is found by exploring.
            var stations = new[]
            {
                new StationSpec("lv1_id_left_ventricle", "Examine the chamber",        new Vector3(5.5f, 0f, 1.5f)),
                new StationSpec("lv1_id_mitral_valve",   "Examine the inflow valve",   new Vector3(-3.5f, 0f, -11f)),
                new StationSpec("lv1_drag_aorta",        "Label the outflow vessel",   new Vector3(2.8f, 0f, 11.5f)),
                new StationSpec("lv1_valve_semilunar",   "Examine the outflow valve",  new Vector3(-2.8f, 0f, 11.5f)),
                new StationSpec("lv1_flow_left_heart",   "Trace the blood flow",       new Vector3(-6.5f, 0f, -5.5f)),
                new StationSpec("lv1_mc_thickest_wall",  "Review the chamber wall",    new Vector3(-10.5f, 0f, 4.5f))
            };

            var objectives = new[]
            {
                ObjectiveSpec.Puzzle("Identify the left ventricle", "lv1_id_left_ventricle"),
                ObjectiveSpec.Puzzle("Identify the mitral valve", "lv1_id_mitral_valve"),
                ObjectiveSpec.Puzzle("Label the aorta", "lv1_drag_aorta"),
                ObjectiveSpec.Puzzle("Identify the semilunar valve", "lv1_valve_semilunar"),
                ObjectiveSpec.Puzzle("Order the left-heart blood flow", "lv1_flow_left_heart"),
                ObjectiveSpec.Puzzle("Answer the chamber-wall question", "lv1_mc_thickest_wall"),
                ObjectiveSpec.Exit("Leave through the aortic valve")
            };

            // Two neutrophils patrol-hunt the chamber; one monocyte plods across
            // the aorta corridor, forcing a detour past the plaque. Placed away
            // from the spawn so the tutorial level does not open with a chase.
            var obstacles = new[]
            {
                ObstacleSpec.Neutrophil(new Vector3(8f, 1f, 5f)),
                ObstacleSpec.Neutrophil(new Vector3(-8f, 1f, 7f)),
                ObstacleSpec.Monocyte(new Vector3(-2f, 1f, 18f),
                                      new Vector3(-2f, 1f, 17f), new Vector3(2f, 1f, 23f))
            };

            AssembleLevel(scene, playerPrefab, env, LevelId.Level1_LeftVentricle, GameConstants.SceneLevel1,
                          objectives, stations,
                          gridSize: new Vector2(46f, 64f), obstacles: obstacles);
        }

        /// <summary>
        /// Level 2 - the cerebral circulation.
        ///
        /// Station placement follows the two routes rather than being scattered:
        /// the questions about the posterior supply sit in the basilar, the
        /// carotid questions sit in the carotids, and the two questions about
        /// collateral flow sit inside the Circle of Willis itself - which the
        /// player can only have reached by taking the collateral route, because
        /// the thrombus seals the direct one.
        /// </summary>
        private static void CreateLevel2Scene(GameObject playerPrefab)
        {
            Scene scene = NewScene();

            EnvironmentFactory.SetupLighting(
                ambient: new Color(0.24f, 0.26f, 0.34f),
                fogColor: new Color(0.05f, 0.06f, 0.11f),
                fogDensity: 0.013f);

            LevelEnvironment env = EnvironmentFactory.BuildCerebralVessels();

            var stations = new[]
            {
                new StationSpec("lv2_id_basilar_artery",     "Examine the vessel",        new Vector3(0f, 0f, -26f)),
                new StationSpec("lv2_flow_carotid",          "Trace the blood flow",      new Vector3(-16f, 0f, -33f)),
                new StationSpec("lv2_drag_internal_carotid", "Label the vessel",          new Vector3(-26f, 0f, -12f)),
                new StationSpec("lv2_mc_ischaemic_stroke",   "Review the blockage",       new Vector3(26f, 0f, -12f)),
                new StationSpec("lv2_id_circle_of_willis",   "Examine the ring",          new Vector3(0f, 0f, -5f)),
                new StationSpec("lv2_mc_collateral",         "Answer the question",       new Vector3(5f, 0f, 3f)),
                new StationSpec("lv2_flow_vertebral",        "Trace the posterior route", new Vector3(-26f, 0f, 0f))
            };

            var objectives = new[]
            {
                ObjectiveSpec.Puzzle("Identify the basilar artery", "lv2_id_basilar_artery"),
                ObjectiveSpec.Puzzle("Order the carotid blood flow", "lv2_flow_carotid"),
                ObjectiveSpec.Puzzle("Label the internal carotid", "lv2_drag_internal_carotid"),
                ObjectiveSpec.Puzzle("Explain the blockage", "lv2_mc_ischaemic_stroke"),
                ObjectiveSpec.Puzzle("Identify the Circle of Willis", "lv2_id_circle_of_willis"),
                ObjectiveSpec.Puzzle("Explain collateral circulation", "lv2_mc_collateral"),
                ObjectiveSpec.Puzzle("Order the vertebral route", "lv2_flow_vertebral"),
                ObjectiveSpec.Exit("Reach the cerebral arteries")
            };

            // Moving obstacles along both open routes, so neither carotid is a
            // free walk. The monocyte patrols the ring, which is the one place
            // every route converges.
            var obstacles = new[]
            {
                ObstacleSpec.Neutrophil(new Vector3(-26f, 1f, -20f)),
                ObstacleSpec.Neutrophil(new Vector3(26f, 1f, -20f)),
                ObstacleSpec.Neutrophil(new Vector3(0f, 1f, 14f)),
                ObstacleSpec.Monocyte(new Vector3(-6f, 1f, 0f),
                                      new Vector3(-6f, 1f, 0f), new Vector3(6f, 1f, 0f))
            };

            AssembleLevel(scene, playerPrefab, env, LevelId.Level2_Brain, GameConstants.SceneLevel2,
                          objectives, stations,
                          gridSize: new Vector2(72f, 84f), obstacles: obstacles);
        }

        /// <summary>
        /// Level 3 - the right ventricle and the start of the pulmonary circuit.
        ///
        /// Carries the highest obstacle density of the three levels (six agents
        /// against Level 1's three) and the hardest question set, which is what
        /// the roadmap asks of the final level.
        /// </summary>
        private static void CreateLevel3Scene(GameObject playerPrefab)
        {
            Scene scene = NewScene();

            EnvironmentFactory.SetupLighting(
                ambient: new Color(0.22f, 0.25f, 0.33f),
                fogColor: new Color(0.05f, 0.07f, 0.12f),
                fogDensity: 0.014f);

            LevelEnvironment env = EnvironmentFactory.BuildRightVentricle();

            var stations = new[]
            {
                new StationSpec("lv3_mc_tricuspid",           "Examine the inflow valve",  new Vector3(0f, 0f, -17f)),
                new StationSpec("lv3_valve_backflow_atrium",  "Examine the valve",         new Vector3(-4f, 0f, -9f)),
                new StationSpec("lv3_id_right_ventricle",     "Examine the chamber",       new Vector3(0f, 0f, 2f)),
                new StationSpec("lv3_flow_right_heart",       "Trace the blood flow",      new Vector3(-8f, 0f, 3f)),
                new StationSpec("lv3_mc_wall_thickness",      "Review the chamber wall",   new Vector3(8f, 0f, -4f)),
                new StationSpec("lv3_mc_pressure",            "Answer the question",       new Vector3(-6f, 0f, -6f)),
                new StationSpec("lv3_drag_pulmonary_artery",  "Label the outflow vessel",  new Vector3(3f, 0f, 9f)),
                new StationSpec("lv3_mc_pulmonary_artery",    "Review the vessel",         new Vector3(0f, 0f, 22f))
            };

            var objectives = new[]
            {
                ObjectiveSpec.Puzzle("Identify the tricuspid valve", "lv3_mc_tricuspid"),
                ObjectiveSpec.Puzzle("Find the valve that stops backflow", "lv3_valve_backflow_atrium"),
                ObjectiveSpec.Puzzle("Identify the right ventricle", "lv3_id_right_ventricle"),
                ObjectiveSpec.Puzzle("Order the right-heart blood flow", "lv3_flow_right_heart"),
                ObjectiveSpec.Puzzle("Explain the wall thickness", "lv3_mc_wall_thickness"),
                ObjectiveSpec.Puzzle("Compare the circuit pressures", "lv3_mc_pressure"),
                ObjectiveSpec.Puzzle("Label the pulmonary artery", "lv3_drag_pulmonary_artery"),
                ObjectiveSpec.Puzzle("Explain why the artery is unusual", "lv3_mc_pulmonary_artery"),
                ObjectiveSpec.Exit("Leave through the pulmonary valve")
            };

            // Highest density in the game: four hunters in the chamber plus two
            // patrolling monocytes, one of them blocking the outflow corridor.
            var obstacles = new[]
            {
                ObstacleSpec.Neutrophil(new Vector3(-7f, 1f, -4f)),
                ObstacleSpec.Neutrophil(new Vector3(7f, 1f, 6f)),
                ObstacleSpec.Neutrophil(new Vector3(-5f, 1f, 8f)),
                ObstacleSpec.Neutrophil(new Vector3(5f, 1f, -7f)),
                ObstacleSpec.Monocyte(new Vector3(0f, 1f, 18f),
                                      new Vector3(-2f, 1f, 16f), new Vector3(2f, 1f, 24f)),
                ObstacleSpec.Monocyte(new Vector3(-9f, 1f, 0f),
                                      new Vector3(-9f, 1f, -6f), new Vector3(-9f, 1f, 6f))
            };

            AssembleLevel(scene, playerPrefab, env, LevelId.Level3_RightVentricle, GameConstants.SceneLevel3,
                          objectives, stations,
                          gridSize: new Vector2(40f, 60f), obstacles: obstacles);
        }


        /// <summary>Adds the player, camera, UI, puzzle systems and stations to a level scene, then saves it.</summary>
        private static void AssembleLevel(Scene scene, GameObject playerPrefab, LevelEnvironment env,
                                          LevelId levelId, string sceneName,
                                          ObjectiveSpec[] objectives, StationSpec[] stations,
                                          Vector2 gridSize, ObstacleSpec[] obstacles)
        {
            // ---- Player ----
            GameObject player = playerPrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab)
                : null;

            if (player != null)
            {
                player.transform.SetPositionAndRotation(env.SpawnPoint.position, env.SpawnPoint.rotation);
            }
            else
            {
                Debug.LogError("[SceneFactory] Player prefab missing - the level will have no player.");
            }

            // ---- Camera ----
            Camera camera = CreateGameplayCamera(LevelBackground);
            var rig = camera.gameObject.AddComponent<OrbitCameraRig>();
            if (player != null)
            {
                using (var w = new EditorWiring(rig)) w.Set("target", player.transform);
                using (var w = new EditorWiring(player.GetComponent<PlayerController>())) w.Set("cameraTransform", camera.transform);
            }

            // ---- UI (HUD, puzzle panel, pause menu, result panels) ----
            PuzzleUI puzzleUI = BuildGameplayUI();

            // ---- Level controller ----
            var controllerGo = new GameObject("LevelController");
            var controller = controllerGo.AddComponent<LevelController>();
            using (var w = new EditorWiring(controller))
            {
                w.SetInt("levelId", (int)levelId);
                w.Set("spawnPoint", env.SpawnPoint);
                w.Set("playerPrefab", playerPrefab);
            }

            // ---- Puzzle + objective systems ----
            var systemsGo = new GameObject("PuzzleSystems");

            var puzzleManager = systemsGo.AddComponent<PuzzleManager>();
            QuestionBank bank = AssetDatabase.LoadAssetAtPath<QuestionBank>(QuestionBankFactory.BankPath(levelId));
            if (bank == null) Debug.LogWarning($"[SceneFactory] No question bank found for {levelId} - puzzles will not load.");

            using (var w = new EditorWiring(puzzleManager))
            {
                w.Set("bank", bank);
                w.Set("puzzleUI", puzzleUI);
            }

            var objectiveManager = systemsGo.AddComponent<ObjectiveManager>();
            WireObjectives(objectiveManager, objectives);

            // Wrong answers spawn leukemic blasts; killing one reveals that
            // question's hint. Scene-level because it instantiates into the level.
            var hostileDirector = systemsGo.AddComponent<HostileSpawnDirector>();
            using (var w = new EditorWiring(hostileDirector))
            {
                w.Set("blastPrefab", _blastPrefab);
            }

            // ---- Puzzle stations ----
            var stationRoot = new GameObject("PuzzleStations");
            foreach (StationSpec spec in stations)
            {
                CreatePuzzleStation(stationRoot.transform, spec);
            }

            // ---- Navigation + obstacles ----
            BuildNavigation(gridSize);
            BuildObstacles(obstacles);

            // ---- Exit trigger ----
            var exitGo = new GameObject("LevelExit");
            exitGo.transform.position = env.ExitAnchor.position;

            var exitBox = exitGo.AddComponent<BoxCollider>();
            exitBox.isTrigger = true;
            exitBox.size = new Vector3(6f, 6f, 2.5f);
            exitBox.center = new Vector3(0f, 2f, 0f);

            GameObject exitVisual = PrefabFactory.CreateBlock(exitGo.transform, "ExitMarker",
                new Vector3(0f, 2f, 0f), new Vector3(2.4f, 0.5f, 2.4f), ProjectAssets.ExitGlow);

            var exitTrigger = exitGo.AddComponent<LevelExitTrigger>();
            using (var w = new EditorWiring(exitTrigger))
            {
                w.Set("levelController", controller);
                w.Set("spinner", exitVisual.transform);
            }

            EnvironmentFactory.CreateWorldLabel(exitGo.transform, "LEVEL EXIT", new Vector3(0f, 4.2f, 0f), 2.6f);

            SaveScene(scene, sceneName);
        }

        // ------------------------------------------------------------------
        // Navigation and obstacles
        // ------------------------------------------------------------------

        /// <summary>
        /// Places the A* grid. Masks are set from the layers created in Phase 1:
        /// floors and walls are Environment, fatty plaque is Obstacle, and both
        /// block a body while only Environment counts as standable ground.
        /// </summary>
        private static void BuildNavigation(Vector2 gridSize)
        {
            var go = new GameObject("AStarPathfinding");

            int environment = LayerMask.NameToLayer(GameConstants.LayerEnvironment);
            int obstacle = LayerMask.NameToLayer(GameConstants.LayerObstacle);

            int groundMask = environment >= 0 ? 1 << environment : ~0;
            int blockingMask = groundMask | (obstacle >= 0 ? 1 << obstacle : 0);

            var manager = go.AddComponent<AStarPathfindingManager>();
            using (var w = new EditorWiring(manager))
            {
                // LayerMask serializes as a plain int, so intValue is correct here.
                w.SetInt("groundMask", groundMask);
                w.SetInt("blockingMask", blockingMask);
                w.SetFloat("nodeRadius", 0.5f);
                w.SetFloat("maxWalkableHeight", 1.5f);
                w.SetFloat("agentRadius", 0.6f);
                w.SetFloat("clearanceHeight", 0.7f);
            }

            // Vector2 needs its own SerializedProperty accessor.
            var so = new SerializedObject(manager);
            SerializedProperty size = so.FindProperty("gridWorldSize");
            if (size != null) size.vector2Value = gridSize;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.AddComponent<ObstacleManager>();
        }

        /// <summary>Instantiates the mobile obstacles and their patrol routes.</summary>
        private static void BuildObstacles(ObstacleSpec[] obstacles)
        {
            if (obstacles == null || obstacles.Length == 0) return;

            var root = new GameObject("Obstacles");

            for (int i = 0; i < obstacles.Length; i++)
            {
                ObstacleSpec spec = obstacles[i];
                GameObject prefab = spec.IsMonocyte ? _monocytePrefab : _neutrophilPrefab;

                if (prefab == null)
                {
                    Debug.LogWarning("[SceneFactory] Obstacle prefab missing - skipping.");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(root.transform, false);
                instance.transform.position = spec.Position;

                if (spec.PatrolPoints == null || spec.PatrolPoints.Length == 0) continue;

                // Patrol waypoints are real transforms so they can be dragged in
                // the editor during playtesting.
                var route = new GameObject($"{instance.name}_Route");
                route.transform.SetParent(root.transform, false);

                var points = new List<Object>();
                for (int p = 0; p < spec.PatrolPoints.Length; p++)
                {
                    var point = new GameObject($"Point_{p}");
                    point.transform.SetParent(route.transform, false);
                    point.transform.position = spec.PatrolPoints[p];
                    points.Add(point.transform);
                }

                using (var w = new EditorWiring(instance.GetComponent<ObstacleAgent>()))
                {
                    w.SetArray("patrolPoints", points);
                }
            }
        }

        // ------------------------------------------------------------------
        // Puzzle stations and objectives
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a clipboard-on-a-post that opens one puzzle.
        ///
        /// The visual blocks keep their colliders, which is what
        /// PlayerInteraction's overlap query finds; PuzzleStation is on the
        /// root, so GetComponentInParent resolves from any of them.
        /// </summary>
        private static GameObject CreatePuzzleStation(Transform parent, StationSpec spec)
        {
            var root = new GameObject($"Station_{spec.PuzzleId}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = spec.Position;

            EnvironmentFactory.CreateBlock(root.transform, "Post", new Vector3(0f, 1.1f, 0f),
                new Vector3(0.3f, 2.2f, 0.3f), ProjectAssets.MuscleWallDark);

            // Bobbing child so the board moves but the post stays planted.
            var bob = new GameObject("Bob");
            bob.transform.SetParent(root.transform, false);
            bob.transform.localPosition = new Vector3(0f, 2.35f, 0f);

            GameObject board = EnvironmentFactory.CreateBlock(bob.transform, "Board", Vector3.zero,
                new Vector3(1.7f, 1.25f, 0.16f), ProjectAssets.StationPending);

            TMP_Text label = EnvironmentFactory.CreateWorldLabel(bob.transform, "?", new Vector3(0f, 0f, -0.2f), 3.2f, 3f);

            var station = root.AddComponent<PuzzleStation>();
            using (var w = new EditorWiring(station))
            {
                w.SetString("puzzleId", spec.PuzzleId);
                w.SetString("promptOverride", spec.Prompt ?? string.Empty);
                w.Set("worldLabel", label);
                w.SetArray("stateRenderers", new Object[] { board.GetComponent<Renderer>() });
                w.Set("pendingMaterial", ProjectAssets.StationPending);
                w.Set("solvedMaterial", ProjectAssets.StationSolved);
                w.Set("bobTransform", bob.transform);
            }

            return root;
        }

        /// <summary>
        /// Fills ObjectiveManager's list. The elements are serializable classes
        /// rather than object references, so each field is set through a
        /// relative SerializedProperty instead of EditorWiring.SetArray.
        /// </summary>
        private static void WireObjectives(ObjectiveManager manager, ObjectiveSpec[] specs)
        {
            var so = new SerializedObject(manager);
            SerializedProperty list = so.FindProperty("objectives");

            if (list == null)
            {
                Debug.LogWarning("[SceneFactory] ObjectiveManager has no 'objectives' field.");
                return;
            }

            list.arraySize = specs.Length;
            for (int i = 0; i < specs.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Description").stringValue = specs[i].Description;
                element.FindPropertyRelative("Kind").intValue = (int)specs[i].Kind;
                element.FindPropertyRelative("PuzzleId").stringValue = specs[i].PuzzleId ?? string.Empty;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Cameras
        // ------------------------------------------------------------------

        private static Camera CreateUiCamera(Color background)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";

            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.orthographic = true;   // nothing 3D to render on a menu screen
            cam.cullingMask = 0;

            return cam;
        }

        private static Camera CreateGameplayCamera(Color background)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 5f, -28f);

            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 160f;      // short far plane keeps culling cheap
            cam.fieldOfView = 62f;

            return cam;
        }

        // ------------------------------------------------------------------
        // Gameplay UI
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates the HUD canvas, the puzzle canvas and the pause/result menu
        /// canvas, fully wired. Returns the PuzzleUI so the caller can hand it
        /// to the level's PuzzleManager.
        /// </summary>
        private static PuzzleUI BuildGameplayUI()
        {
            UIFactory.CreateEventSystem();

            // ---------------- HUD ----------------
            Canvas hudCanvas = UIFactory.CreateCanvas("UI_HUD", 0);
            var hudGroup = hudCanvas.gameObject.AddComponent<CanvasGroup>();
            hudGroup.interactable = false;
            hudGroup.blocksRaycasts = false;      // the HUD must never eat clicks
            var hud = hudCanvas.gameObject.AddComponent<GameplayHUD>();

            // Blood Count, top left
            RectTransform bloodPanel = UIFactory.CreateRect(hudCanvas.transform, "BloodCount");
            UIFactory.SetRect(bloodPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f, -60f), new Vector2(400f, 86f));
            UIFactory.CreatePanel(bloodPanel, "Background", new Color(0f, 0f, 0f, 0.45f));

            TMP_Text bloodTitle = UIFactory.CreateText(bloodPanel, "Title", "BLOOD COUNT", 20f,
                TextAlignmentOptions.TopLeft, UIFactory.ColorTextDim, FontStyles.Bold);
            UIFactory.SetRect(bloodTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -18f), new Vector2(-28f, 24f));

            Image barBackground = UIFactory.CreateImage(bloodPanel, "BarBackground", new Color(0.20f, 0.06f, 0.08f), UIFactory.BackgroundSprite);
            UIFactory.SetRect(barBackground.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 26f), new Vector2(-28f, 26f));

            Image barFill = UIFactory.CreateImage(barBackground.transform, "BarFill", new Color(0.85f, 0.18f, 0.22f), UIFactory.BackgroundSprite);
            UIFactory.Stretch(barFill.rectTransform, 2f);
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            barFill.fillAmount = 1f;

            TMP_Text bloodValue = UIFactory.CreateText(bloodPanel, "Value", "100 / 100", 20f,
                TextAlignmentOptions.MidlineRight, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.SetRect(bloodValue.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -18f), new Vector2(-28f, 24f));

            // Session info, top right
            RectTransform infoPanel = UIFactory.CreateRect(hudCanvas.transform, "SessionInfo");
            UIFactory.SetRect(infoPanel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-200f, -66f), new Vector2(360f, 120f));

            TMP_Text levelLabel = UIFactory.CreateText(infoPanel, "LevelLabel", "Level 1 - Left Ventricle", 24f,
                TextAlignmentOptions.TopRight, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.SetRect(levelLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -16f), new Vector2(0f, 30f));

            TMP_Text difficultyLabel = UIFactory.CreateText(infoPanel, "DifficultyLabel", "Difficulty: Easy", 22f,
                TextAlignmentOptions.TopRight, UIFactory.ColorAccent);
            UIFactory.SetRect(difficultyLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -48f), new Vector2(0f, 28f));

            TMP_Text scoreLabel = UIFactory.CreateText(infoPanel, "ScoreLabel", "Score: 0", 22f,
                TextAlignmentOptions.TopRight, UIFactory.ColorTextDim);
            UIFactory.SetRect(scoreLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -76f), new Vector2(0f, 28f));

            TMP_Text fpsLabel = UIFactory.CreateText(infoPanel, "FpsLabel", "-- FPS", 20f,
                TextAlignmentOptions.TopRight, UIFactory.ColorTextDim);
            UIFactory.SetRect(fpsLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -104f), new Vector2(0f, 26f));

            // Objective clipboard, bottom left
            ObjectiveBoardUI board = BuildObjectiveBoard(hudCanvas.transform, out TMP_Text boardTitle, out List<Object> rows);

            // Hint indicator, bottom centre
            RectTransform hintPanel = UIFactory.CreateRect(hudCanvas.transform, "HintIndicator");
            UIFactory.SetRect(hintPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(900f, 64f));
            UIFactory.CreatePanel(hintPanel, "Background", new Color(0.25f, 0.20f, 0.05f, 0.85f));

            TMP_Text hintLabel = UIFactory.CreateText(hintPanel, "HintLabel", "Hint", 24f,
                TextAlignmentOptions.Center, new Color(1f, 0.93f, 0.6f));
            UIFactory.Stretch(hintLabel.rectTransform, 12f);
            hintPanel.gameObject.SetActive(false);

            // Interaction prompt, just above the hint line
            RectTransform promptPanel = UIFactory.CreateRect(hudCanvas.transform, "InteractionPrompt");
            UIFactory.SetRect(promptPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 205f), new Vector2(560f, 54f));
            UIFactory.CreatePanel(promptPanel, "Background", new Color(0.05f, 0.10f, 0.14f, 0.85f));

            TMP_Text promptLabel = UIFactory.CreateText(promptPanel, "PromptLabel", "[E]  Examine", 24f,
                TextAlignmentOptions.Center, new Color(0.75f, 0.92f, 1f), FontStyles.Bold);
            UIFactory.Stretch(promptLabel.rectTransform, 10f);
            promptPanel.gameObject.SetActive(false);

            using (var w = new EditorWiring(hud))
            {
                w.Set("canvasGroup", hudGroup);
                w.Set("bloodCountFill", barFill);
                w.Set("bloodCountLabel", bloodValue);
                w.Set("levelLabel", levelLabel);
                w.Set("difficultyLabel", difficultyLabel);
                w.Set("scoreLabel", scoreLabel);
                w.Set("objectiveBoard", board);
                w.Set("hintIndicator", hintPanel.gameObject);
                w.Set("hintLabel", hintLabel);
                w.Set("interactionPrompt", promptPanel.gameObject);
                w.Set("interactionLabel", promptLabel);
                w.Set("fpsLabel", fpsLabel);
            }

            using (var w = new EditorWiring(board))
            {
                w.Set("titleLabel", boardTitle);
                w.SetArray("rows", rows);
            }

            // ---------------- Menus (pause + results) ----------------
            Canvas menuCanvas = UIFactory.CreateCanvas("UI_Menus", 10);
            var pause = menuCanvas.gameObject.AddComponent<PauseMenuUI>();
            var result = menuCanvas.gameObject.AddComponent<LevelResultUI>();

            SettingsPanel settings = BuildSettingsPanel(menuCanvas.transform);
            settings.gameObject.SetActive(false);

            RectTransform pausePanel = BuildPauseMenu(menuCanvas.transform, out Button resume, out Button restart,
                                                      out Button settingsButton, out Button exitButton);
            pausePanel.gameObject.SetActive(false);

            using (var w = new EditorWiring(pause))
            {
                w.Set("pausePanel", pausePanel.gameObject);
                w.Set("settingsPanel", settings);
                w.Set("resumeButton", resume);
                w.Set("restartButton", restart);
                w.Set("settingsButton", settingsButton);
                w.Set("exitButton", exitButton);
            }

            BuildResultPanels(menuCanvas.transform, result);

            // ---------------- Puzzle panel ----------------
            // Its own canvas at order 5: above the HUD so the panel is never
            // obscured, below the pause/result menus so those still cover it.
            Canvas puzzleCanvas = UIFactory.CreateCanvas("UI_Puzzle", 5);
            return BuildPuzzleUI(puzzleCanvas.transform);
        }

        /// <summary>
        /// The puzzle panel. Anchored to the bottom of the screen so the upper
        /// half of the view stays clear - structure puzzles are answered by
        /// clicking or dropping a label on the chamber above it.
        /// </summary>
        private static PuzzleUI BuildPuzzleUI(Transform parent)
        {
            RectTransform panel = UIFactory.CreateRect(parent, "PuzzlePanel");
            UIFactory.SetRect(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 240f), new Vector2(1560f, 440f));
            UIFactory.CreatePanel(panel, "Background", new Color(0.09f, 0.07f, 0.10f, 0.95f));

            // PuzzleUI goes on the CANVAS, not on the panel it toggles.
            //
            // A MonoBehaviour placed on the GameObject it deactivates stops
            // receiving Update the moment it hides itself, and never receives
            // Awake at all while the object starts inactive. That combination
            // soft-locked the game: Show() activated the panel, Awake ran for
            // the first time and immediately switched it back off, so the panel
            // never appeared, Update never ran, and the Escape handler that
            // returns the player to GameState.Playing could never fire.
            //
            // PauseMenuUI and LevelResultUI already sit on their canvas with
            // child panels; this makes PuzzleUI consistent with them.
            var puzzleUI = parent.gameObject.AddComponent<PuzzleUI>();

            // ---- Header row ----
            TMP_Text header = UIFactory.CreateText(panel, "Header", "IDENTIFY THE STRUCTURE", 26f,
                TextAlignmentOptions.MidlineLeft, UIFactory.ColorAccent, FontStyles.Bold);
            UIFactory.SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(390f, -34f), new Vector2(720f, 38f));

            // No HINT button: hints are earned by destroying the leukemic blast
            // that a wrong answer spawns, or offered automatically by the DDA.
            Button closeButton = UIFactory.CreateButton(panel, "Btn_Close", "CLOSE  [ESC]", new Vector2(180f, 44f), 18f);
            UIFactory.SetRect(closeButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-100f, -36f), new Vector2(180f, 44f));

            // ---- Prompt ----
            TMP_Text prompt = UIFactory.CreateText(panel, "Prompt", "", 26f,
                TextAlignmentOptions.TopLeft, UIFactory.ColorTextLight);
            UIFactory.SetRect(prompt.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -96f), new Vector2(-70f, 70f));

            // ---- Structure section ----
            RectTransform structureSection = UIFactory.CreateRect(panel, "StructureSection");
            UIFactory.SetRect(structureSection, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(1440f, 150f));

            TMP_Text structureInstruction = UIFactory.CreateText(structureSection, "Instruction",
                "Click the correct structure in the chamber.", 22f,
                TextAlignmentOptions.Top, UIFactory.ColorTextDim, FontStyles.Italic);
            UIFactory.SetRect(structureInstruction.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -18f), new Vector2(0f, 34f));

            DraggableLabel dragChip = BuildDragChip(structureSection);

            // ---- Multiple choice ----
            RectTransform optionsSection = UIFactory.CreateRect(panel, "OptionsSection");
            UIFactory.SetRect(optionsSection, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -252f), new Vector2(1420f, 216f));
            UIFactory.AddVerticalLayout(optionsSection.gameObject, 8f);

            var optionButtons = new List<Object>();
            for (int i = 0; i < 4; i++)
            {
                Button option = UIFactory.CreateButton(optionsSection, $"Btn_Option{i}", "", new Vector2(1420f, 46f), 22f);
                AddLayoutHeight(option.gameObject, 46f);
                optionButtons.Add(option);
            }

            // ---- Blood flow sequence ----
            RectTransform sequenceSection = UIFactory.CreateRect(panel, "SequenceSection");
            UIFactory.SetRect(sequenceSection, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -240f), new Vector2(1460f, 200f));

            TMP_Text sequenceOrder = UIFactory.CreateText(sequenceSection, "OrderLabel",
                "<i>Click the steps in order...</i>", 24f,
                TextAlignmentOptions.Center, new Color(0.75f, 0.92f, 1f));
            UIFactory.SetRect(sequenceOrder.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -22f), new Vector2(0f, 40f));

            RectTransform stepRow = UIFactory.CreateRect(sequenceSection, "Steps");
            UIFactory.SetRect(stepRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(1440f, 56f));
            var stepLayout = stepRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            stepLayout.spacing = 8f;
            stepLayout.childAlignment = TextAnchor.MiddleCenter;
            stepLayout.childControlWidth = true;
            stepLayout.childControlHeight = true;
            stepLayout.childForceExpandWidth = true;
            stepLayout.childForceExpandHeight = false;

            var sequenceButtons = new List<Object>();
            for (int i = 0; i < 6; i++)
            {
                Button step = UIFactory.CreateButton(stepRow, $"Btn_Step{i}", "", new Vector2(230f, 52f), 18f);
                AddLayoutHeight(step.gameObject, 52f);
                sequenceButtons.Add(step);
            }

            Button sequenceSubmit = UIFactory.CreateButton(sequenceSection, "Btn_SequenceSubmit", "SUBMIT ORDER", new Vector2(260f, 46f), 20f);
            UIFactory.SetRect(sequenceSubmit.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-145f, -166f), new Vector2(260f, 46f));

            Button sequenceReset = UIFactory.CreateButton(sequenceSection, "Btn_SequenceReset", "CLEAR", new Vector2(180f, 46f), 20f);
            UIFactory.SetRect(sequenceReset.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(145f, -166f), new Vector2(180f, 46f));

            // ---- Footer ----
            TMP_Text feedback = UIFactory.CreateText(panel, "Feedback", "", 23f,
                TextAlignmentOptions.Top, UIFactory.ColorTextLight);
            UIFactory.SetRect(feedback.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 56f), new Vector2(-70f, 96f));

            TMP_Text attempts = UIFactory.CreateText(panel, "Attempts", "", 19f,
                TextAlignmentOptions.BottomRight, UIFactory.ColorTextDim);
            UIFactory.SetRect(attempts.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-200f, 16f), new Vector2(360f, 26f));

            using (var w = new EditorWiring(puzzleUI))
            {
                w.Set("panelRoot", panel.gameObject);
                w.Set("headerLabel", header);
                w.Set("promptLabel", prompt);
                w.Set("feedbackLabel", feedback);
                w.Set("attemptsLabel", attempts);
                w.Set("structureSection", structureSection.gameObject);
                w.Set("structureInstruction", structureInstruction);
                w.Set("dragChip", dragChip);
                w.Set("optionsSection", optionsSection.gameObject);
                w.SetArray("optionButtons", optionButtons);
                w.Set("sequenceSection", sequenceSection.gameObject);
                w.SetArray("sequenceButtons", sequenceButtons);
                w.Set("sequenceOrderLabel", sequenceOrder);
                w.Set("sequenceSubmitButton", sequenceSubmit);
                w.Set("sequenceResetButton", sequenceReset);
                w.Set("closeButton", closeButton);
            }

            panel.gameObject.SetActive(false);
            return puzzleUI;
        }

        /// <summary>The draggable anatomical label chip.</summary>
        private static DraggableLabel BuildDragChip(Transform parent)
        {
            var go = new GameObject("DragChip", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            UIFactory.SetRect(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(380f, 56f));

            var image = go.GetComponent<Image>();
            image.sprite = UIFactory.RoundedSprite;
            image.type = Image.Type.Sliced;
            image.color = UIFactory.ColorAccent;
            image.raycastTarget = true;   // must receive the drag

            TMP_Text label = UIFactory.CreateText(go.transform, "Label", "Label", 24f,
                TextAlignmentOptions.Center, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.Stretch(label.rectTransform, 8f);

            var chip = go.AddComponent<DraggableLabel>();
            using (var w = new EditorWiring(chip))
            {
                w.Set("label", label);
                w.Set("canvasGroup", go.GetComponent<CanvasGroup>());
            }

            return chip;
        }

        private static ObjectiveBoardUI BuildObjectiveBoard(Transform parent, out TMP_Text titleLabel, out List<Object> rows)
        {
            RectTransform boardRect = UIFactory.CreateRect(parent, "ObjectiveBoard");
            UIFactory.SetRect(boardRect, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(255f, 235f), new Vector2(470f, 330f));

            UIFactory.CreatePanel(boardRect, "Clipboard", UIFactory.ColorClipboard);

            // The little metal clip at the top of a medical clipboard.
            Image clip = UIFactory.CreateImage(boardRect, "Clip", new Color(0.55f, 0.56f, 0.58f), UIFactory.RoundedSprite);
            UIFactory.SetRect(clip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 6f), new Vector2(120f, 26f));

            titleLabel = UIFactory.CreateText(boardRect, "Title", "CURRENT OBJECTIVE", 22f,
                TextAlignmentOptions.TopLeft, new Color(0.35f, 0.10f, 0.14f), FontStyles.Bold);
            UIFactory.SetRect(titleLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -26f), new Vector2(-40f, 28f));

            RectTransform list = UIFactory.CreateRect(boardRect, "Rows");
            UIFactory.SetRect(list, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -178f), new Vector2(-44f, 250f));
            UIFactory.AddVerticalLayout(list.gameObject, 4f, new RectOffset(6, 6, 0, 0), TextAnchor.UpperLeft);

            // Eight rows cover Level 1's six puzzle objectives plus the exit, with
            // headroom. Unused rows hide themselves.
            rows = new List<Object>();
            for (int i = 0; i < 8; i++)
            {
                TMP_Text row = UIFactory.CreateText(list, $"Row_{i}", string.Empty, 19f,
                    TextAlignmentOptions.MidlineLeft, UIFactory.ColorTextDark);
                AddLayoutHeight(row.gameObject, 26f);
                row.gameObject.SetActive(false);
                rows.Add(row);
            }

            return boardRect.gameObject.AddComponent<ObjectiveBoardUI>();
        }

        private static RectTransform BuildPauseMenu(Transform parent, out Button resume, out Button restart,
                                                    out Button settings, out Button exit)
        {
            RectTransform panel = UIFactory.CreateRect(parent, "PausePanel");
            UIFactory.Stretch(panel);

            UIFactory.CreatePanel(panel, "Dim", new Color(0.03f, 0.01f, 0.02f, 0.82f));

            TMP_Text title = UIFactory.CreateText(panel, "Title", "PAUSED", 56f,
                TextAlignmentOptions.Center, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.SetRect(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(700f, 70f));

            RectTransform column = UIFactory.CreateRect(panel, "Buttons");
            UIFactory.SetRect(column, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(440f, 300f));
            UIFactory.AddVerticalLayout(column.gameObject, 14f);

            resume = UIFactory.CreateButton(column, "Btn_Resume", "RESUME", new Vector2(440f, 62f));
            restart = UIFactory.CreateButton(column, "Btn_Restart", "RESTART LEVEL", new Vector2(440f, 62f));
            settings = UIFactory.CreateButton(column, "Btn_Settings", "SETTINGS", new Vector2(440f, 62f));
            exit = UIFactory.CreateButton(column, "Btn_Exit", "EXIT TO MAIN MENU", new Vector2(440f, 62f));

            AddLayoutHeight(resume.gameObject, 62f);
            AddLayoutHeight(restart.gameObject, 62f);
            AddLayoutHeight(settings.gameObject, 62f);
            AddLayoutHeight(exit.gameObject, 62f);

            return panel;
        }

        private static void BuildResultPanels(Transform parent, LevelResultUI result)
        {
            // ---- Level complete ----
            RectTransform complete = UIFactory.CreateRect(parent, "LevelCompletePanel");
            UIFactory.Stretch(complete);
            UIFactory.CreatePanel(complete, "Dim", new Color(0.02f, 0.03f, 0.02f, 0.9f));

            TMP_Text completeTitle = UIFactory.CreateText(complete, "Title", "LEVEL COMPLETE", 54f,
                TextAlignmentOptions.Center, new Color(0.6f, 0.9f, 0.7f), FontStyles.Bold);
            UIFactory.SetRect(completeTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1000f, 70f));

            TMP_Text completeSummary = UIFactory.CreateText(complete, "Summary", string.Empty, 24f,
                TextAlignmentOptions.TopLeft, UIFactory.ColorTextLight);
            UIFactory.SetRect(completeSummary.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(620f, 300f));

            RectTransform completeButtons = UIFactory.CreateRect(complete, "Buttons");
            UIFactory.SetRect(completeButtons, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -220f), new Vector2(440f, 140f));
            UIFactory.AddVerticalLayout(completeButtons.gameObject, 12f);

            Button next = UIFactory.CreateButton(completeButtons, "Btn_Next", "NEXT LEVEL", new Vector2(440f, 60f));
            Button completeMenu = UIFactory.CreateButton(completeButtons, "Btn_Menu", "MAIN MENU", new Vector2(440f, 60f));
            AddLayoutHeight(next.gameObject, 60f);
            AddLayoutHeight(completeMenu.gameObject, 60f);

            complete.gameObject.SetActive(false);

            // ---- Attempt failed ----
            RectTransform failed = UIFactory.CreateRect(parent, "LevelFailedPanel");
            UIFactory.Stretch(failed);
            UIFactory.CreatePanel(failed, "Dim", new Color(0.10f, 0.01f, 0.02f, 0.9f));

            TMP_Text failedTitle = UIFactory.CreateText(failed, "Title", "ATTEMPT FAILED", 54f,
                TextAlignmentOptions.Center, new Color(0.95f, 0.5f, 0.45f), FontStyles.Bold);
            UIFactory.SetRect(failedTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1000f, 70f));

            TMP_Text failedSummary = UIFactory.CreateText(failed, "Summary", string.Empty, 24f,
                TextAlignmentOptions.TopLeft, UIFactory.ColorTextLight);
            UIFactory.SetRect(failedSummary.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(620f, 320f));

            RectTransform failedButtons = UIFactory.CreateRect(failed, "Buttons");
            UIFactory.SetRect(failedButtons, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -230f), new Vector2(440f, 140f));
            UIFactory.AddVerticalLayout(failedButtons.gameObject, 12f);

            Button retry = UIFactory.CreateButton(failedButtons, "Btn_Retry", "RETRY LEVEL", new Vector2(440f, 60f));
            Button failedMenu = UIFactory.CreateButton(failedButtons, "Btn_Menu", "MAIN MENU", new Vector2(440f, 60f));
            AddLayoutHeight(retry.gameObject, 60f);
            AddLayoutHeight(failedMenu.gameObject, 60f);

            failed.gameObject.SetActive(false);

            using (var w = new EditorWiring(result))
            {
                w.Set("completePanel", complete.gameObject);
                w.Set("failedPanel", failed.gameObject);
                w.Set("completeTitleLabel", completeTitle);
                w.Set("completeSummaryLabel", completeSummary);
                w.Set("nextLevelButton", next);
                w.Set("completeMenuButton", completeMenu);
                w.Set("failedSummaryLabel", failedSummary);
                w.Set("retryButton", retry);
                w.Set("failedMenuButton", failedMenu);
            }
        }

        // ------------------------------------------------------------------
        // Settings panel (shared by the main menu and the pause menu)
        // ------------------------------------------------------------------

        private static SettingsPanel BuildSettingsPanel(Transform parent)
        {
            RectTransform panel = UIFactory.CreateRect(parent, "SettingsPanel");
            UIFactory.SetRect(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 620f));
            UIFactory.CreatePanel(panel, "Background", UIFactory.ColorPanel);

            TMP_Text title = UIFactory.CreateText(panel, "Title", "SETTINGS", 42f,
                TextAlignmentOptions.Center, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(760f, 54f));

            // Master volume
            TMP_Text volumeLabel = UIFactory.CreateText(panel, "VolumeLabel", "Master volume", 24f,
                TextAlignmentOptions.MidlineLeft, UIFactory.ColorTextDim);
            UIFactory.SetRect(volumeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(230f, -140f), new Vector2(360f, 32f));

            TMP_Text volumeValue = UIFactory.CreateText(panel, "VolumeValue", "80%", 24f,
                TextAlignmentOptions.MidlineRight, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.SetRect(volumeValue.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-90f, -140f), new Vector2(140f, 32f));

            Slider volumeSlider = UIFactory.CreateSlider(panel, "VolumeSlider", 0f, 1f, 0.8f, new Vector2(660f, 34f));
            UIFactory.SetRect(volumeSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -184f), new Vector2(660f, 34f));

            // Mouse sensitivity
            TMP_Text sensLabel = UIFactory.CreateText(panel, "SensitivityLabel", "Camera sensitivity", 24f,
                TextAlignmentOptions.MidlineLeft, UIFactory.ColorTextDim);
            UIFactory.SetRect(sensLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(230f, -240f), new Vector2(360f, 32f));

            TMP_Text sensValue = UIFactory.CreateText(panel, "SensitivityValue", "220", 24f,
                TextAlignmentOptions.MidlineRight, UIFactory.ColorTextLight, FontStyles.Bold);
            UIFactory.SetRect(sensValue.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-90f, -240f), new Vector2(140f, 32f));

            Slider sensSlider = UIFactory.CreateSlider(panel, "SensitivitySlider", 60f, 500f, 220f, new Vector2(660f, 34f));
            UIFactory.SetRect(sensSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -284f), new Vector2(660f, 34f));

            // Toggles
            Toggle fullscreen = UIFactory.CreateToggle(panel, "FullscreenToggle", "Fullscreen", true, new Vector2(320f, 36f));
            UIFactory.SetRect(fullscreen.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(240f, -340f), new Vector2(320f, 36f));

            Toggle invertY = UIFactory.CreateToggle(panel, "InvertYToggle", "Invert camera Y axis", false, new Vector2(400f, 36f));
            UIFactory.SetRect(invertY.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(280f, -386f), new Vector2(400f, 36f));

            TMP_Text savePath = UIFactory.CreateText(panel, "SavePathLabel", string.Empty, 16f,
                TextAlignmentOptions.Center, UIFactory.ColorTextDim);
            UIFactory.SetRect(savePath.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 128f), new Vector2(760f, 40f));

            Button reset = UIFactory.CreateButton(panel, "Btn_ResetProgress", "RESET LOCAL PROGRESS", new Vector2(360f, 48f), 20f);
            UIFactory.SetRect(reset.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-190f, 68f), new Vector2(360f, 48f));

            Button back = UIFactory.CreateButton(panel, "Btn_Back", "BACK", new Vector2(360f, 48f), 20f);
            UIFactory.SetRect(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(190f, 68f), new Vector2(360f, 48f));

            var settings = panel.gameObject.AddComponent<SettingsPanel>();
            using (var w = new EditorWiring(settings))
            {
                w.Set("masterVolumeSlider", volumeSlider);
                w.Set("sensitivitySlider", sensSlider);
                w.Set("fullscreenToggle", fullscreen);
                w.Set("invertYToggle", invertY);
                w.Set("backButton", back);
                w.Set("resetProgressButton", reset);
                w.Set("volumeValueLabel", volumeValue);
                w.Set("sensitivityValueLabel", sensValue);
                w.Set("savePathLabel", savePath);
            }

            return settings;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Pins a fixed height on an element inside a VerticalLayoutGroup.
        /// Without this the layout group collapses every child to zero height.
        /// </summary>
        private static void AddLayoutHeight(GameObject target, float height)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null) element = target.AddComponent<LayoutElement>();

            element.minHeight = height;
            element.preferredHeight = height;
        }
    }
}
