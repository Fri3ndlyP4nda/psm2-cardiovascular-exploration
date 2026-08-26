using System.IO;
using Cardio.Core;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Creates the non-scene assets the project needs: folder structure, tags,
    /// layers, shared materials and a guaranteed-present TMP font.
    ///
    /// Everything here is idempotent - running the setup tool twice produces
    /// the same result and never duplicates an asset.
    /// </summary>
    public static class ProjectAssets
    {
        public const string MaterialsFolder = "Assets/Materials";
        public const string PrefabsFolder = "Assets/Prefabs";
        public const string ScenesFolder = "Assets/Scenes";
        public const string FontsFolder = "Assets/UI/Fonts";

        /// <summary>The folder layout from the PSM1 architecture section.</summary>
        private static readonly string[] Folders =
        {
            "Assets/Scenes",
            "Assets/Prefabs",
            "Assets/Prefabs/Player",
            "Assets/Prefabs/Environment",
            "Assets/Prefabs/UI",
            "Assets/Materials",
            "Assets/Models",
            "Assets/UI",
            "Assets/UI/Fonts",
            "Assets/UI/Sprites",
            "Assets/Audio",
            "Assets/Resources",
            "Assets/Data",              // ScriptableObject question banks (Phase 2)
            "Assets/Scripts",
            "Assets/Scripts/Core",
            "Assets/Scripts/Player",
            "Assets/Scripts/Gameplay",
            "Assets/Scripts/AI",        // Phase 5: A* pathfinding
            "Assets/Scripts/DDA",       // Phase 4: dynamic difficulty
            "Assets/Scripts/UI",
            "Assets/Scripts/Firebase",  // Phase 7: auth + Firestore
            "Assets/Scripts/Editor",
            "Assets/Scripts/Editor/Generation"
        };

        private static readonly string[] Tags =
        {
            GameConstants.TagHazard,
            GameConstants.TagInteractable
            // "Player" is a Unity built-in tag and must not be added again.
        };

        private static readonly string[] Layers =
        {
            GameConstants.LayerEnvironment,
            GameConstants.LayerObstacle,
            GameConstants.LayerPlayer
        };

        // ------------------------------------------------------------------
        // Folders
        // ------------------------------------------------------------------

        public static void CreateFolders()
        {
            foreach (string folder in Folders)
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;

                string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
                string leaf = Path.GetFileName(folder);
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) continue;

                AssetDatabase.CreateFolder(parent, leaf);
            }

            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------
        // Tags and layers
        // ------------------------------------------------------------------

        public static void CreateTagsAndLayers()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[ProjectAssets] Could not open TagManager.asset - tags and layers were not created.");
                return;
            }

            var tagManager = new SerializedObject(assets[0]);
            AddTags(tagManager);
            AddLayers(tagManager);
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddTags(SerializedObject tagManager)
        {
            SerializedProperty tagsProp = tagManager.FindProperty("tags");
            if (tagsProp == null) return;

            foreach (string tag in Tags)
            {
                bool exists = false;
                for (int i = 0; i < tagsProp.arraySize; i++)
                {
                    if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) { exists = true; break; }
                }
                if (exists) continue;

                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            }
        }

        private static void AddLayers(SerializedObject tagManager)
        {
            SerializedProperty layersProp = tagManager.FindProperty("layers");
            if (layersProp == null) return;

            foreach (string layer in Layers)
            {
                bool exists = false;
                for (int i = 0; i < layersProp.arraySize; i++)
                {
                    if (layersProp.GetArrayElementAtIndex(i).stringValue == layer) { exists = true; break; }
                }
                if (exists) continue;

                // Indices 0-7 are reserved by Unity for built-in layers.
                bool placed = false;
                for (int i = 8; i < layersProp.arraySize; i++)
                {
                    SerializedProperty slot = layersProp.GetArrayElementAtIndex(i);
                    if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                    slot.stringValue = layer;
                    placed = true;
                    break;
                }

                if (!placed) Debug.LogWarning($"[ProjectAssets] No free user layer slot for '{layer}'.");
            }
        }

        // ------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------

        /// <summary>
        /// Finds a lit shader that exists in whichever render pipeline the
        /// project uses. Prevents the classic "everything is magenta" result
        /// when the template does not match the expected pipeline.
        /// </summary>
        public static Shader FindLitShader()
        {
            Shader s = Shader.Find("Standard");                              // Built-in RP
            if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");  // URP
            if (s == null) s = Shader.Find("HDRP/Lit");                       // HDRP
            if (s == null) s = Shader.Find("Diffuse");                        // last resort
            return s;
        }

        /// <summary>Creates (or updates) a flat, unlit-looking material suited to voxel art.</summary>
        public static Material CreateMaterial(string name, Color color, float smoothness = 0.05f, Color? emission = null)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                mat = new Material(FindLitShader());
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);   // URP/HDRP name
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

            // Emission is kept enabled (black by default) so HintManager can tint
            // a structure at runtime without needing to enable the keyword then.
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", emission ?? Color.black);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---- Shared palette (voxel style: flat, high contrast, cheap) ----
        public static Material MuscleWall => CreateMaterial("M_MuscleWall", new Color(0.42f, 0.10f, 0.13f));
        public static Material MuscleWallDark => CreateMaterial("M_MuscleWallDark", new Color(0.30f, 0.07f, 0.10f));
        public static Material Endocardium => CreateMaterial("M_Endocardium", new Color(0.62f, 0.24f, 0.26f));
        public static Material ValveTissue => CreateMaterial("M_ValveTissue", new Color(0.90f, 0.86f, 0.80f));
        public static Material Plaque => CreateMaterial("M_FattyPlaque", new Color(0.86f, 0.76f, 0.36f));
        public static Material BloodCell => CreateMaterial("M_BloodCell", new Color(0.80f, 0.13f, 0.17f));
        public static Material CellHighlight => CreateMaterial("M_BloodCellDark", new Color(0.58f, 0.08f, 0.12f));
        public static Material EyeWhite => CreateMaterial("M_EyeWhite", new Color(0.96f, 0.96f, 0.96f));
        public static Material EyePupil => CreateMaterial("M_EyePupil", new Color(0.09f, 0.09f, 0.11f));
        public static Material Oxygenated => CreateMaterial("M_Oxygenated", new Color(0.85f, 0.20f, 0.22f));
        public static Material Deoxygenated => CreateMaterial("M_Deoxygenated", new Color(0.24f, 0.36f, 0.66f));
        public static Material ExitGlow => CreateMaterial("M_ExitMarker", new Color(0.35f, 0.85f, 0.70f), 0.1f, new Color(0.10f, 0.35f, 0.28f));

        // Immune cells: neutrophils pale and aggressive, monocytes darker and bulky.
        public static Material Neutrophil => CreateMaterial("M_Neutrophil", new Color(0.85f, 0.87f, 0.72f));
        public static Material NeutrophilNucleus => CreateMaterial("M_NeutrophilNucleus", new Color(0.52f, 0.34f, 0.62f));
        public static Material Monocyte => CreateMaterial("M_Monocyte", new Color(0.60f, 0.66f, 0.78f));
        public static Material MonocyteNucleus => CreateMaterial("M_MonocyteNucleus", new Color(0.32f, 0.38f, 0.55f));

        // Leukemic blast: sickly violet with an oversized, faintly glowing nucleus.
        // The high nucleus-to-cytoplasm ratio is the real diagnostic marker of a
        // blast cell, so the thing that makes it look wrong is also what makes
        // it medically wrong.
        public static Material BlastBody => CreateMaterial("M_LeukemicBlast", new Color(0.62f, 0.45f, 0.70f), 0.05f, new Color(0.16f, 0.04f, 0.20f));
        public static Material BlastNucleus => CreateMaterial("M_LeukemicNucleus", new Color(0.22f, 0.12f, 0.30f), 0.1f, new Color(0.22f, 0.03f, 0.26f));

        // Puzzle stations: pale clipboard while unanswered, green once solved.
        public static Material StationPending => CreateMaterial("M_StationPending", new Color(0.88f, 0.85f, 0.74f), 0.05f, new Color(0.16f, 0.14f, 0.06f));
        public static Material StationSolved => CreateMaterial("M_StationSolved", new Color(0.42f, 0.72f, 0.48f), 0.1f, new Color(0.06f, 0.20f, 0.10f));

        // ------------------------------------------------------------------
        // Fonts
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns a usable TMP font asset, creating one from a built-in Unity
        /// font if the TMP Essential Resources have not been imported.
        /// Without this the generated UI would render as empty boxes.
        /// </summary>
        public static TMP_FontAsset ResolveFont()
        {
            if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;

            var bundled = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (bundled != null) return bundled;

            string path = $"{FontsFolder}/CardioDefault SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;

            Font source = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                          ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (source == null)
            {
                Debug.LogError("[ProjectAssets] No built-in font available. Import the TMP Essential Resources " +
                               "(Window > TextMeshPro > Import TMP Essential Resources) and run the setup tool again.");
                return null;
            }

            TMP_FontAsset created = TMP_FontAsset.CreateFontAsset(source);
            if (created == null) return null;

            created.name = "CardioDefault SDF";
            AssetDatabase.CreateAsset(created, path);

            // A font asset only survives a domain reload if its material and
            // atlas texture are stored as sub-assets of the same file.
            if (created.material != null) AssetDatabase.AddObjectToAsset(created.material, created);
            if (created.atlasTextures != null && created.atlasTextures.Length > 0 && created.atlasTextures[0] != null)
            {
                AssetDatabase.AddObjectToAsset(created.atlasTextures[0], created);
            }

            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
