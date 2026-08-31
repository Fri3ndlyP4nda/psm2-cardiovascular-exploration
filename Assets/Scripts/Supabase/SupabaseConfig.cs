using UnityEngine;

namespace Cardio.Backend
{
    /// <summary>
    /// Connection details for the Supabase project.
    ///
    /// THE ANON KEY IS COMMITTED ON PURPOSE. It is not a secret: Supabase issues
    /// it specifically to be embedded in client applications, and it carries the
    /// `anon` role, which grants nothing on its own. Every table in this project
    /// has Row Level Security enabled with policies keyed on `auth.uid()`, so the
    /// key can only ever reach rows belonging to the caller's own authenticated
    /// user. This was verified against the live project before shipping: an
    /// unauthenticated insert is rejected with Postgres error 42501,
    /// "new row violates row-level security policy".
    ///
    /// That conditional matters. Without RLS the same key would grant full read
    /// and write access to anyone who ran `strings` on the built executable. The
    /// safety comes from the policies, not from hiding the key - which is why
    /// hiding it would have bought nothing while making the project harder to
    /// build from a clean clone.
    ///
    /// THE SERVICE_ROLE KEY MUST NEVER APPEAR HERE. It bypasses RLS entirely.
    /// <see cref="SupabaseManager"/> refuses to start if the configured key is
    /// not an anon key, so a paste of the wrong one fails loudly at boot rather
    /// than silently shipping a database with no access control.
    /// </summary>
    [CreateAssetMenu(fileName = "SupabaseConfig", menuName = "PSM2/Supabase Config")]
    public class SupabaseConfig : ScriptableObject
    {
        [Header("Project")]
        [Tooltip("Base project URL, with no trailing path. e.g. https://abcdefg.supabase.co")]
        public string ProjectUrl = "";

        [Tooltip("The anon / public key. Safe to commit - see the class comment. NEVER the service_role key.")]
        [TextArea(3, 6)]
        public string AnonKey = "";

        [Header("Behaviour")]
        [Tooltip("Turn off to run entirely offline. Everything queues locally and nothing is uploaded.")]
        public bool SyncEnabled = true;

        [Tooltip("Seconds before a request is treated as failed and the payload is queued.")]
        [Range(2, 60)] public int TimeoutSeconds = 10;

        /// <summary>REST base, e.g. https://x.supabase.co/rest/v1</summary>
        public string RestUrl => $"{Trimmed}/rest/v1";

        /// <summary>Auth base, e.g. https://x.supabase.co/auth/v1</summary>
        public string AuthUrl => $"{Trimmed}/auth/v1";

        /// <summary>
        /// Project URL without a trailing slash or REST path.
        ///
        /// Tolerated because the dashboard shows the URL in more than one form
        /// and it is easy to paste ".../rest/v1/" by mistake; silently building
        /// ".../rest/v1/rest/v1/session_logs" would be a confusing 404.
        /// </summary>
        private string Trimmed
        {
            get
            {
                string url = (ProjectUrl ?? string.Empty).Trim().TrimEnd('/');
                if (url.EndsWith("/rest/v1")) url = url.Substring(0, url.Length - "/rest/v1".Length);
                if (url.EndsWith("/auth/v1")) url = url.Substring(0, url.Length - "/auth/v1".Length);
                return url.TrimEnd('/');
            }
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectUrl) && !string.IsNullOrWhiteSpace(AnonKey);

        /// <summary>
        /// True if the key's JWT payload claims the `anon` role.
        ///
        /// Decodes rather than trusts: a service_role key looks identical at a
        /// glance and pasting one here would disable every protection the RLS
        /// policies provide.
        /// </summary>
        public bool KeyLooksLikeAnonKey(out string detail)
        {
            detail = string.Empty;
            string key = (AnonKey ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(key)) { detail = "no key set"; return false; }

            // Newer projects issue publishable keys instead of JWTs. These carry
            // no role claim to inspect, but are anon-equivalent by construction.
            if (key.StartsWith("sb_publishable_")) return true;
            if (key.StartsWith("sb_secret_")) { detail = "this is a SECRET key"; return false; }

            string[] parts = key.Split('.');
            if (parts.Length != 3) { detail = "not a JWT and not a publishable key"; return false; }

            string payload;
            try { payload = System.Text.Encoding.UTF8.GetString(DecodeBase64Url(parts[1])); }
            catch { detail = "JWT payload could not be decoded"; return false; }

            if (payload.Contains("\"role\":\"service_role\""))
            {
                detail = "this is a SERVICE_ROLE key - it bypasses Row Level Security";
                return false;
            }

            if (!payload.Contains("\"role\":\"anon\""))
            {
                detail = "JWT does not claim the anon role";
                return false;
            }

            return true;
        }

        private static byte[] DecodeBase64Url(string segment)
        {
            string s = segment.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return System.Convert.FromBase64String(s);
        }
    }
}
