using System.IO;
using Cardio.Backend;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Creates the Supabase config asset if it is missing.
    ///
    /// Non-destructive by design, like the question banks: if a config already
    /// exists it is left alone, so re-running the project build never clobbers
    /// a URL and key someone pasted in. Use the Reseed menu item to overwrite
    /// deliberately.
    /// </summary>
    public static class SupabaseConfigFactory
    {
        private const string Folder = "Assets/Resources/Supabase";
        private const string AssetPath = Folder + "/SupabaseConfig.asset";

        // The project this build talks to. The anon key is committed on purpose;
        // see the comment on SupabaseConfig for why that is safe and what it
        // depends on (Row Level Security being enabled, which it is).
        private const string DefaultUrl = "https://vevmzzgikpqyjoudunho.supabase.co";
        private const string DefaultAnonKey =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InZldm16emdpa3BxeWpvdWR1bmhvIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODgxNjgwMTEsImV4cCI6MjEwMzc0NDAxMX0." +
            "siEVJhhP1nIMEwqk481UGm8Xh-UOywxFw2qFgwznL4U";

        public static void CreateIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<SupabaseConfig>(AssetPath) != null)
            {
                Debug.Log("[PSM2] Supabase config already exists - left untouched.");
                return;
            }

            Write();
        }

        [MenuItem("PSM2/Content/Reseed Supabase Config (Destructive)", priority = 36)]
        public static void Reseed()
        {
            bool proceed = Application.isBatchMode || EditorUtility.DisplayDialog(
                "Reseed Supabase config",
                "This overwrites Assets/Resources/Supabase/SupabaseConfig.asset with the " +
                "project URL and anon key compiled into SupabaseConfigFactory.\n\nContinue?",
                "Reseed", "Cancel");

            if (!proceed) return;
            Write();
        }

        private static void Write()
        {
            Directory.CreateDirectory(Folder);

            var config = ScriptableObject.CreateInstance<SupabaseConfig>();
            config.ProjectUrl = DefaultUrl;
            config.AnonKey = DefaultAnonKey;
            config.SyncEnabled = true;
            config.TimeoutSeconds = 10;

            AssetDatabase.CreateAsset(config, AssetPath);
            AssetDatabase.SaveAssets();

            if (!config.KeyLooksLikeAnonKey(out string detail))
            {
                Debug.LogError($"[PSM2] The compiled-in Supabase key is not an anon key ({detail}). " +
                               "This must be fixed before shipping - a service_role key bypasses RLS.");
                return;
            }

            Debug.Log($"[PSM2] Supabase config written to {AssetPath} (anon key verified).");
        }
    }
}
