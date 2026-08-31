using System;
using System.Collections;
using Cardio.Core;
using UnityEngine;

namespace Cardio.Backend
{
    /// <summary>
    /// Supabase authentication, anonymous-sign-in flavour.
    ///
    /// Replaces Firebase Auth. The player never sees a login form: the game
    /// signs in silently on first launch and reuses that identity forever after.
    /// That was chosen over email/password because the UAT participants are
    /// students being handed a laptop for forty minutes, and asking them to
    /// invent credentials adds friction for a benefit the study does not need.
    ///
    /// THE SESSION IS PERSISTED, AND THAT IS THE WHOLE POINT. An anonymous
    /// sign-in creates a brand new user every time it is called. Without saving
    /// the refresh token, every launch would strand the previous launch's data
    /// under a user nobody can log back into, and a participant's second session
    /// would look like a different person. The refresh token in the save file is
    /// what makes "the same anonymous player" a real thing.
    ///
    /// Security note, stated rather than glossed: that refresh token sits in
    /// plaintext in psm2_progress.json. Anyone with read access to the machine
    /// could use it to write session logs as that anonymous user. Row Level
    /// Security still confines them to that one user's rows, and for a local
    /// single-player study on a supervised machine that is a proportionate
    /// trade. It would not be for anything holding personal data.
    /// </summary>
    [DisallowMultipleComponent]
    public class AuthenticationManager : MonoBehaviour
    {
        public static AuthenticationManager Instance { get; private set; }

        [Header("Live state (read-only)")]
        [SerializeField] private bool signedIn;
        [SerializeField] private string userId = "";
        [SerializeField] private string lastError = "";

        private string _accessToken = "";

        /// <summary>Raised whenever sign-in state changes, so the queue can flush on reconnect.</summary>
        public event Action<bool> SignedInChanged;

        public bool IsSignedIn => signedIn && !string.IsNullOrEmpty(_accessToken);
        public string UserId => userId;
        public string AccessToken => _accessToken;
        public string LastError => lastError;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (SupabaseManager.Instance != null && SupabaseManager.Instance.IsEnabled)
            {
                StartCoroutine(SignIn());
            }
        }

        /// <summary>
        /// Signs in, reusing the stored anonymous identity if there is one.
        ///
        /// Refresh first, sign up second. Getting that order wrong would mint a
        /// new anonymous user on every launch while the old one still existed.
        /// </summary>
        public IEnumerator SignIn()
        {
            SupabaseManager backend = SupabaseManager.Instance;
            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;

            if (backend == null || !backend.IsEnabled || save == null)
            {
                SetSignedIn(false, "backend disabled");
                yield break;
            }

            string storedRefresh = save.Progress.SupabaseRefreshToken;

            if (!string.IsNullOrEmpty(storedRefresh))
            {
                yield return Refresh(storedRefresh);
                if (IsSignedIn) yield break;

                // A refresh can fail because the token expired or the project
                // was reset. Falling through to a fresh anonymous sign-in is
                // better than leaving the player unable to sync at all.
                Debug.Log("[Supabase] Stored session could not be refreshed; creating a new anonymous user.");
            }

            yield return SignUpAnonymously();
        }

        private IEnumerator SignUpAnonymously()
        {
            SupabaseManager backend = SupabaseManager.Instance;

            var headers = new System.Collections.Generic.Dictionary<string, string>
            {
                { "apikey", backend.Config.AnonKey }
            };

            BackendResponse response = default;
            yield return backend.Send("POST", $"{backend.Config.AuthUrl}/signup", "{}", headers, r => response = r);

            if (!response.Success)
            {
                // The most likely cause by far, and worth naming explicitly so
                // it is not mistaken for a network problem.
                if (response.Body.Contains("anonymous_provider_disabled"))
                {
                    SetSignedIn(false, "anonymous sign-ins are disabled in the Supabase dashboard " +
                                       "(Authentication > Sign In / Providers > Anonymous sign-ins)");
                }
                else
                {
                    SetSignedIn(false, response.IsTransportFailure
                        ? "offline"
                        : $"sign-up failed ({response.StatusCode}): {Truncate(response.Body)}");
                }
                yield break;
            }

            ApplySession(response.Body);
        }

        private IEnumerator Refresh(string refreshToken)
        {
            SupabaseManager backend = SupabaseManager.Instance;

            var headers = new System.Collections.Generic.Dictionary<string, string>
            {
                { "apikey", backend.Config.AnonKey }
            };

            string body = "{\"refresh_token\":\"" + Escape(refreshToken) + "\"}";

            BackendResponse response = default;
            yield return backend.Send("POST", $"{backend.Config.AuthUrl}/token?grant_type=refresh_token",
                                      body, headers, r => response = r);

            if (!response.Success)
            {
                SetSignedIn(false, response.IsTransportFailure ? "offline" : "refresh rejected");
                yield break;
            }

            ApplySession(response.Body);
        }

        /// <summary>Parses an auth response and persists the identity.</summary>
        private void ApplySession(string json)
        {
            AuthResponse parsed;
            try { parsed = JsonUtility.FromJson<AuthResponse>(json); }
            catch (Exception e)
            {
                SetSignedIn(false, $"unreadable auth response: {e.Message}");
                return;
            }

            if (parsed == null || string.IsNullOrEmpty(parsed.access_token))
            {
                SetSignedIn(false, "auth response carried no access token");
                return;
            }

            _accessToken = parsed.access_token;
            userId = parsed.user != null ? parsed.user.id : string.Empty;

            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;
            if (save != null)
            {
                save.Progress.SupabaseUserId = userId;
                save.Progress.SupabaseRefreshToken = parsed.refresh_token ?? string.Empty;
                save.SaveNow();
            }

            SetSignedIn(true, string.Empty);
            Debug.Log($"[Supabase] Signed in anonymously as {userId}.");
        }

        private void SetSignedIn(bool value, string error)
        {
            bool changed = signedIn != value;
            signedIn = value;
            lastError = error ?? string.Empty;

            if (!value) _accessToken = string.Empty;
            if (changed) SignedInChanged?.Invoke(value);
        }

        private static string Escape(string s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        private static string Truncate(string s) => string.IsNullOrEmpty(s) || s.Length <= 160 ? s : s.Substring(0, 160);

        // Shapes for JsonUtility. Field names must match the GoTrue payload.
        [Serializable] private class AuthResponse
        {
            public string access_token;
            public string refresh_token;
            public AuthUser user;
        }

        [Serializable] private class AuthUser
        {
            public string id;
            public bool is_anonymous;
        }
    }
}
