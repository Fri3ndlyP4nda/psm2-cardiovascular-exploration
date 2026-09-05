using System.Collections.Generic;
using Cardio.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Verifies the rendering decisions the 60 FPS target depends on (TC-23).
    ///
    /// READ THIS BEFORE QUOTING IT: this does **not** measure frame rate, and
    /// passing it is not evidence the game runs at 60 FPS. Batch mode has no
    /// renderer, so frame timing measured here would be meaningless - that
    /// measurement stays MANUAL REQUIRED and needs the HUD counter on a real
    /// machine.
    ///
    /// What it does check is that the *design decisions* ARCHITECTURE.md section
    /// 8 credits for hitting the target are actually present in the generated
    /// scenes: no real-time shadows anywhere, every renderer opted out of
    /// shadow casting and receiving, environment geometry flagged for static
    /// batching, a small shared material set, and a short far clip plane.
    ///
    /// That distinction matters. A regression here - someone re-enabling
    /// shadows, or geometry losing its static flag - would quietly cost frames
    /// on the target laptop, and this catches that without a human. It answers
    /// "are the things we said we did still done", not "is it fast".
    /// </summary>
    public static class PerformanceBudgetCheck
    {
        private static int _passed;
        private static int _failed;

        /// <summary>Far clip the gameplay camera is generated with.</summary>
        private const float ExpectedFarClip = 160f;

        /// <summary>Upper bound on distinct materials in a level, per the shared-material decision.</summary>
        /// <summary>
        /// Shared materials allowed per scene, counted across every MeshRenderer.
        ///
        /// Raised from 16 to 17 when the player gained M_OxygenBurst, the swing
        /// burst. Recorded rather than quietly bumped: the budget exists to keep
        /// draw batches down for the 60 FPS target, and one more material is one
        /// more batch, so it should only move for something that earns it.
        ///
        /// This one does. The attack is the only route to a hint at the higher
        /// tiers and previously had no visual at all, so a player could not tell a
        /// landed swing from a missed one. The cost is also close to the smallest
        /// it could be: a single small sphere whose renderer is disabled except for
        /// the ~0.3s a swing is on screen.
        /// </summary>
        private const int MaterialBudget = 17;

        [MenuItem("PSM2/Diagnostics/Run Performance Budget Check", priority = 74)]
        public static void Run()
        {
            _passed = 0;
            _failed = 0;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[PSM2 PerfCheck] Cancelled - unsaved changes in the open scene.");
                return;
            }

            foreach (string sceneName in GameConstants.LevelScenes)
            {
                CheckLevel(sceneName);
            }

            string summary = $"[PSM2 PerfCheck] {_passed} passed, {_failed} failed. " +
                             "(Design checks only - actual FPS is MANUAL REQUIRED.)";
            if (_failed == 0) Debug.Log(summary);
            else Debug.LogError(summary);
        }

        private static void CheckLevel(string sceneName)
        {
            string scenePath = $"{ProjectAssets.ScenesFolder}/{sceneName}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Fail($"{sceneName}: scene not found");
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // ---- Lighting: one directional light, no real-time shadows ----
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            int directional = 0;
            int shadowCasting = 0;

            foreach (Light light in lights)
            {
                if (light.type == LightType.Directional) directional++;
                if (light.shadows != LightShadows.None) shadowCasting++;
            }

            True($"{sceneName}: exactly one directional light ({directional})", directional == 1);
            True($"{sceneName}: no light casts real-time shadows ({shadowCasting} do)", shadowCasting == 0);
            True($"{sceneName}: fog is on (hides the open chamber tops, so no ceiling geometry)", RenderSettings.fog);

            // ---- Renderers: shadows off, static batching on ----
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            int casting = 0, receiving = 0, notStatic = 0;
            var materials = new HashSet<Material>();
            var nonStaticEnvironment = new List<string>();

            foreach (Renderer r in renderers)
            {
                // UI and text renderers are not environment geometry and are not
                // expected to carry the static flags.
                bool isEnvironment = r.GetComponent<MeshRenderer>() != null && r.GetComponent<TMPro.TMP_Text>() == null;
                if (!isEnvironment) continue;

                if (r.shadowCastingMode != ShadowCastingMode.Off) casting++;
                if (r.receiveShadows) receiving++;

                if (!GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic))
                {
                    notStatic++;
                    if (InEnvironment(r.transform)) nonStaticEnvironment.Add(PathOf(r.transform));
                }

                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null) materials.Add(m);
                }
            }

            True($"{sceneName}: no renderer casts shadows ({casting} do)", casting == 0);
            True($"{sceneName}: no renderer receives shadows ({receiving} do)", receiving == 0);
            True($"{sceneName}: shared material count within budget ({materials.Count} of {MaterialBudget})",
                 materials.Count <= MaterialBudget);

            // Scoped to the generated environment hierarchy on purpose. Agents,
            // puzzle stations and the player all move and are correctly not
            // static, and their number varies per level - an absolute count
            // across the whole scene would just be a threshold to tune, not a
            // fact about the build. What must hold is that nothing under
            // Environment_* lost its batching flag.
            True($"{sceneName}: all environment geometry is static-batched " +
                 $"({nonStaticEnvironment.Count} stragglers of {notStatic} non-static overall)",
                 nonStaticEnvironment.Count == 0);

            foreach (string path in nonStaticEnvironment)
            {
                Debug.LogError($"[PSM2 PerfCheck]   non-static environment renderer: {path}");
            }

            // ---- Camera: short far clip ----
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            bool foundGameplayCamera = false;

            foreach (Camera cam in cameras)
            {
                if (cam.orthographic) continue;
                foundGameplayCamera = true;

                True($"{sceneName}: camera far clip is {ExpectedFarClip} (is {cam.farClipPlane})",
                     Mathf.Approximately(cam.farClipPlane, ExpectedFarClip));
            }

            True($"{sceneName}: has a perspective gameplay camera", foundGameplayCamera);

            Debug.Log($"[PSM2 PerfCheck] {sceneName}: {renderers.Length} renderers, " +
                      $"{materials.Count} materials, {notStatic} non-static, {lights.Length} lights.");
        }

        /// <summary>True if this transform sits under a generated Environment_* root.</summary>
        private static bool InEnvironment(Transform t)
        {
            for (Transform current = t; current != null; current = current.parent)
            {
                if (current.name.StartsWith("Environment_")) return true;
            }
            return false;
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static void True(string label, bool condition)
        {
            if (condition) { _passed++; return; }
            Fail(label);
        }

        private static void Fail(string label)
        {
            _failed++;
            Debug.LogError($"[PSM2 PerfCheck] FAIL {label}");
        }
    }
}
