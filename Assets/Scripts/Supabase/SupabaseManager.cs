using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Cardio.Backend
{
    /// <summary>
    /// The REST client. Owns the config, the transport, and nothing else.
    ///
    /// Replaces the Firebase SDK entirely. Supabase is PostgREST plus GoTrue
    /// over HTTPS, so a few hundred lines of UnityWebRequest does what a
    /// several-hundred-megabyte SDK would have, with no platform caveat on a
    /// Windows desktop build and no native plugins to ship.
    ///
    /// It deliberately knows nothing about sessions, levels or auth flow. It
    /// sends requests and reports what came back; <see cref="AuthenticationManager"/>
    /// and <see cref="SessionLogManager"/> layer meaning on top.
    /// </summary>
    [DisallowMultipleComponent]
    public class SupabaseManager : MonoBehaviour
    {
        public static SupabaseManager Instance { get; private set; }

        private const string ConfigResourcePath = "Supabase/SupabaseConfig";

        [Header("Live state (read-only)")]
        [SerializeField] private bool configured;
        [SerializeField] private bool lastRequestReachedServer;

        private ISupabaseTransport _transport;
        private SupabaseConfig _config;

        public SupabaseConfig Config => _config;

        /// <summary>False when there is no usable config, or sync is switched off.</summary>
        public bool IsEnabled => _config != null && _config.IsConfigured && _config.SyncEnabled;

        /// <summary>
        /// Whether the last attempt actually reached Supabase.
        ///
        /// The game never asks "is there internet" - that question cannot be
        /// answered reliably and `Application.internetReachability` reports the
        /// adapter, not whether the host is up. What matters is whether the last
        /// real request got an answer, which is what this records.
        /// </summary>
        public bool LastRequestReachedServer => lastRequestReachedServer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            _transport ??= new UnityWebRequestTransport();
            _config = Resources.Load<SupabaseConfig>(ConfigResourcePath);

            if (_config == null)
            {
                Debug.LogWarning($"[Supabase] No config at Resources/{ConfigResourcePath}. " +
                                 "Sync is off; everything queues locally. " +
                                 "Run PSM2 > Setup > Build or Rebuild Project to generate one.");
                configured = false;
                return;
            }

            if (!_config.KeyLooksLikeAnonKey(out string detail))
            {
                // Loud and disabling, not a warning. A service_role key in a
                // shipped client removes every access control on the database.
                Debug.LogError($"[Supabase] REFUSING TO START: the configured key is not an anon key ({detail}). " +
                               "Sync is disabled. Replace it with the anon/public key from " +
                               "Dashboard > Project Settings > API.");
                _config.SyncEnabled = false;
                configured = false;
                return;
            }

            configured = _config.IsConfigured;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Replaces the transport. Tests use this to simulate being offline.</summary>
        public void SetTransport(ISupabaseTransport transport) => _transport = transport;

        /// <summary>Replaces the config. Tests use this rather than shipping a fixture asset.</summary>
        public void SetConfig(SupabaseConfig config)
        {
            _config = config;
            configured = config != null && config.IsConfigured;
        }

        /// <summary>Headers every PostgREST call needs.</summary>
        public Dictionary<string, string> RestHeaders(string accessToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "apikey", _config != null ? _config.AnonKey : string.Empty }
            };

            // Falls back to the anon key so an unauthenticated call still carries
            // a bearer token; RLS will refuse it, which is the correct outcome
            // and a clearer failure than a malformed request.
            headers["Authorization"] = "Bearer " +
                (string.IsNullOrEmpty(accessToken) ? (_config != null ? _config.AnonKey : string.Empty) : accessToken);

            return headers;
        }

        /// <summary>Sends a request and reports the result. Never throws.</summary>
        public IEnumerator Send(string method, string url, string jsonBody,
                                IDictionary<string, string> headers, Action<BackendResponse> onComplete)
        {
            if (_transport == null)
            {
                onComplete?.Invoke(BackendResponse.Offline("no transport"));
                yield break;
            }

            int timeout = _config != null ? _config.TimeoutSeconds : 10;

            BackendResponse captured = BackendResponse.Offline("no response");
            yield return _transport.Send(method, url, jsonBody, headers, timeout, r => captured = r);

            lastRequestReachedServer = !captured.IsTransportFailure;
            onComplete?.Invoke(captured);
        }
    }
}
