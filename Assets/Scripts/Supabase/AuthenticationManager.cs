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

        [Header("Retry")]
        [Tooltip("First sign-in is delayed by a random slice of this, to break up the burst " +
                 "when a room full of machines launches the build at the same moment.")]
        [SerializeField, Range(0f, 30f)] private float initialStaggerSeconds = 4f;

        [Tooltip("How many times sign-in is retried before giving up for this launch.")]
        [SerializeField, Range(1, 10)] private int maxSignInAttempts = 6;

        [SerializeField, Range(1f, 30f)] private float baseRetrySeconds = 3f;
        [SerializeField, Range(10f, 600f)] private float maxRetrySeconds = 120f;

        [Header("Live state (read-only)")]
        [SerializeField] private bool signedIn;
        [SerializeField] private string userId = "";
        [SerializeField] private string lastError = "";
        [SerializeField] private int signInAttempts;

        private string _accessToken = "";

        /// <summary>True when the last attempt failed to reach Supabase at all.</summary>
        private bool _lastAttemptWasTransportFailure;

        /// <summary>True when the failure is a project setting no retry can fix.</summary>
        private bool _configurationIsBroken;

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
                StartCoroutine(SignInWithRetry());
            }
        }

        /// <summary>
        /// Signs in, and keeps trying if it does not work the first time.
        ///
        /// The single attempt this replaced was the difference between a study day
        /// working and quietly not working. Supabase caps anonymous sign-ins at 30
        /// per hour per IP, and a campus network puts every machine behind one
        /// address, so a cohort larger than thirty guarantees rejections. With one
        /// attempt those players simply never sync for the whole session, and
        /// nothing on screen says so.
        ///
        /// Two things make retrying safe rather than a denial-of-service on our own
        /// project: the delay doubles each time, and it is jittered - so machines
        /// that failed together do not come back together.
        /// </summary>
        public IEnumerator SignInWithRetry()
        {
            // Everyone launches on the invigilator's word. Spread the first
            // attempt out before it becomes a synchronised burst.
            yield return new WaitForSecondsRealtime(UnityEngine.Random.Range(0f, initialStaggerSeconds));

            for (int attempt = 0; attempt < maxSignInAttempts; attempt++)
            {
                signInAttempts = attempt + 1;
                yield return SignIn();

                if (IsSignedIn) yield break;

                // Anonymous sign-ins being switched off in the dashboard is not
                // something waiting will fix.
                if (_configurationIsBroken) yield break;

                if (attempt + 1 >= maxSignInAttempts) break;

                float backoff = Mathf.Min(baseRetrySeconds * Mathf.Pow(2f, attempt), maxRetrySeconds);
                float jittered = backoff * UnityEngine.Random.Range(0.5f, 1.5f);

                Debug.Log($"[Supabase] Sign-in attempt {attempt + 1} failed ({lastError}); " +
                          $"retrying in {jittered:0.0}s.");
                yield return new WaitForSecondsRealtime(jittered);
            }

            Debug.LogWarning($"[Supabase] Giving up on sign-in after {signInAttempts} attempts " +
                             $"({lastError}). Play continues; rows stay queued locally.");
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

                // Only a server that actually answered and rejected the token
                // justifies minting a new identity. If we simply could not reach
                // Supabase we have learned nothing about the stored session, and
                // replacing it would orphan every row already uploaded under it -
                // turning one flaky launch into a participant who reads as two
                // different people in the data.
                if (_lastAttemptWasTransportFailure)
                {
                    SetSignedIn(false, "offline - keeping the stored identity");
                    yield break;
                }

                Debug.Log("[Supabase] Stored session was rejected by the server; creating a new anonymous user.");
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

            _lastAttemptWasTransportFailure = response.IsTransportFailure;

            if (!response.Success)
            {
                // The most likely cause by far, and worth naming explicitly so
                // it is not mistaken for a network problem.
                if (response.Body.Contains("anonymous_provider_disabled"))
                {
                    _configurationIsBroken = true;
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

            _lastAttemptWasTransportFailure = response.IsTransportFailure;

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

            _lastAttemptWasTransportFailure = false;
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
