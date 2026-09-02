using System;
using System.Collections;
using System.Collections.Generic;
using Cardio.Core;
using UnityEngine;

namespace Cardio.Backend
{
    /// <summary>
    /// Writes finished attempts to the SESSION_LOGS table, and owns the offline
    /// queue that makes that safe to fail.
    ///
    /// Replaces FirestoreManager. Behaviour is PSM1 NFR4 verbatim: on failure,
    /// serialise the session into <see cref="PlayerProgress.PendingSessionLogs"/>,
    /// tell the player, keep playing, and flush when connectivity returns.
    ///
    /// THIS IS NOT THE DASHBOARD'S DATA. Phase 9's
    /// <see cref="PlayerProgress.SessionHistory"/> is a separate list that is
    /// never drained, because the dashboard has to keep showing a player their
    /// own history whether or not it ever reached a server. This queue is the
    /// opposite: it exists to be emptied. Merging them would mean a successful
    /// sync silently wiped the dashboard, which is exactly the bug the two-list
    /// split was designed to prevent.
    /// </summary>
    [DisallowMultipleComponent]
    public class SessionLogManager : MonoBehaviour
    {
        public static SessionLogManager Instance { get; private set; }

        [Header("Live state (read-only)")]
        [SerializeField] private int queuedCount;
        [SerializeField] private int uploadedThisSession;
        [SerializeField] private string lastStatus = "idle";

        /// <summary>Raised when the queue length changes, so the HUD can say something honest.</summary>
        public event Action<int> QueueChanged;

        public int QueuedCount => queuedCount;
        public int UploadedThisSession => uploadedThisSession;
        public string LastStatus => lastStatus;

        private bool _flushing;
        private bool _moreWorkArrived;

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
            RefreshQueueCount();

            // Flushing on sign-in is the "connectivity returned" trigger. There
            // is no reliable way to ask the OS whether the internet works, but a
            // successful sign-in is proof that Supabase was reachable a moment
            // ago, which is the same information and is not a guess.
            AuthenticationManager auth = AuthenticationManager.Instance;
            if (auth != null) auth.SignedInChanged += OnSignedInChanged;

            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;
            if (save != null) save.SessionRecorded += OnSessionRecorded;
        }

        private void OnDisable()
        {
            AuthenticationManager auth = AuthenticationManager.Instance;
            if (auth != null) auth.SignedInChanged -= OnSignedInChanged;

            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;
            if (save != null) save.SessionRecorded -= OnSessionRecorded;
        }

        private void OnSignedInChanged(bool isSignedIn)
        {
            if (isSignedIn && isActiveAndEnabled) StartCoroutine(FlushQueue());
        }

        /// <summary>A level just finished. Try to upload it; queue it if that fails.</summary>
        private void OnSessionRecorded(SessionRecord record)
        {
            if (record == null) return;

            string payload = BuildPayload(record);

            // Queue first, upload second, and let a success dequeue it. Doing it
            // the other way round means a crash between "upload failed" and
            // "write the queue" loses the attempt entirely.
            Enqueue(payload);

            if (isActiveAndEnabled) StartCoroutine(FlushQueue());
        }

        /// <summary>
        /// Serialises one attempt into a SESSION_LOGS row.
        ///
        /// Column names are the Postgres snake_case ones. The mapping worth
        /// naming is failed_attempts: PSM1 calls this FailedAttempts and it
        /// means "Blood Count reached zero", which is C#'s LevelFailures - NOT
        /// IncorrectAnswers, which counts wrong answers. Conflating the two
        /// would quietly corrupt the evaluation data.
        /// </summary>
        public string BuildPayload(SessionRecord record)
        {
            string userId = AuthenticationManager.Instance != null
                ? AuthenticationManager.Instance.UserId
                : string.Empty;

            if (string.IsNullOrEmpty(userId))
            {
                SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;
                if (save != null) userId = save.Progress.SupabaseUserId;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append('{');
            Append(sb, "user_id", userId); sb.Append(',');
            AppendNum(sb, "current_level", record.Level); sb.Append(',');
            AppendNum(sb, "average_accuracy", record.Accuracy01); sb.Append(',');
            AppendNum(sb, "avg_response_time", record.AverageResponseSeconds); sb.Append(',');
            Append(sb, "final_difficulty_tier", TierName(record.FinalDifficulty)); sb.Append(',');
            AppendNum(sb, "hints_used", record.HintsUsed); sb.Append(',');
            AppendNum(sb, "failed_attempts", record.LevelFailures); sb.Append(',');
            Append(sb, "session_date", string.IsNullOrEmpty(record.DateUtc)
                ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                : record.DateUtc);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Adds a payload to the queue and persists it immediately.</summary>
        public void Enqueue(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return;

            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;
            if (save == null) return;

            save.Progress.PendingSessionLogs.Add(payload);
            save.SaveNow();
            RefreshQueueCount();
        }

        /// <summary>
        /// Uploads queued rows oldest-first, stopping at the first one that
        /// cannot be sent.
        ///
        /// Stopping rather than skipping keeps the queue in order and avoids
        /// hammering a server that is plainly not answering. A row rejected on
        /// its own merits (4xx) is dropped instead, because retrying a malformed
        /// row forever would block every row behind it.
        /// </summary>
        public IEnumerator FlushQueue()
        {
            // A flush is already running. Rather than drop this request, mark
            // that more work arrived so the running flush loops again before it
            // finishes.
            //
            // Without this there is a real race: a row enqueued after the loop's
            // last count check but before the guard clears would sit in the
            // queue until some later trigger happened to fire. Found by the live
            // round-trip test, where signing in starts a flush and the caller
            // then enqueues into it.
            if (_flushing)
            {
                _moreWorkArrived = true;
                yield break;
            }

            SupabaseManager backend = SupabaseManager.Instance;
            AuthenticationManager auth = AuthenticationManager.Instance;
            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;

            if (save == null) yield break;

            if (backend == null || !backend.IsEnabled)
            {
                SetStatus($"offline - {save.Progress.PendingSessionLogs.Count} queued");
                yield break;
            }

            if (auth == null || !auth.IsSignedIn)
            {
                SetStatus($"not signed in - {save.Progress.PendingSessionLogs.Count} queued");
                yield break;
            }

            _flushing = true;
            _moreWorkArrived = false;

            try
            {
                while (save.Progress.PendingSessionLogs.Count > 0 || _moreWorkArrived)
                {
                    _moreWorkArrived = false;
                    if (save.Progress.PendingSessionLogs.Count == 0) break;

                    string payload = save.Progress.PendingSessionLogs[0];

                    BackendResponse response = default;
                    yield return backend.Send("POST", $"{backend.Config.RestUrl}/session_logs",
                                              payload, backend.RestHeaders(auth.AccessToken),
                                              r => response = r);

                    if (response.Success)
                    {
                        DequeueIfStillThere(save, payload);
                        uploadedThisSession++;
                        RefreshQueueCount();
                        continue;
                    }

                    if (response.IsTransportFailure)
                    {
                        SetStatus($"offline - {save.Progress.PendingSessionLogs.Count} queued for later");
                        yield break;
                    }

                    // Rejected on its own merits. Keeping it would block the
                    // queue forever, so drop it and say so rather than silently.
                    Debug.LogWarning($"[Supabase] Dropping a session log the server rejected " +
                                     $"({response.StatusCode}): {response.Body}");
                    DequeueIfStillThere(save, payload);
                    RefreshQueueCount();
                }

                SetStatus(uploadedThisSession > 0
                    ? $"synced ({uploadedThisSession} this session)"
                    : "nothing to sync");
            }
            finally
            {
                _flushing = false;
            }
        }

        /// <summary>
        /// Removes a payload from the queue, but only if it is still the row we
        /// uploaded.
        ///
        /// A blind RemoveAt(0) after the await is unsafe. The upload yields, and
        /// during that yield the queue can be emptied or replaced entirely -
        /// ResetProgress assigns a whole new PlayerProgress, so even the list
        /// object may differ from the one the payload was read from. Removing by
        /// index then either throws or silently discards somebody else's row.
        ///
        /// Found by FullLoopFunctionalTests, which resets progress while a flush
        /// was in flight and produced an ArgumentOutOfRangeException.
        /// </summary>
        private static void DequeueIfStillThere(SaveManager save, string payload)
        {
            List<string> queue = save.Progress.PendingSessionLogs;

            if (queue.Count > 0 && queue[0] == payload) queue.RemoveAt(0);
            else queue.Remove(payload);   // no-op if it is already gone

            save.SaveNow();
        }

        private void RefreshQueueCount()
        {
            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;
            int count = save != null ? save.Progress.PendingSessionLogs.Count : 0;

            if (count == queuedCount) return;
            queuedCount = count;
            QueueChanged?.Invoke(count);
        }

        private void SetStatus(string status) => lastStatus = status;

        private static string TierName(int tier)
        {
            switch (tier)
            {
                case 1: return "Medium";
                case 2: return "Hard";
                default: return "Easy";
            }
        }

        private static void Append(System.Text.StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append("\":\"")
              .Append((value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""))
              .Append('"');
        }

        private static void AppendNum(System.Text.StringBuilder sb, string key, float value)
        {
            sb.Append('"').Append(key).Append("\":")
              .Append(value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
