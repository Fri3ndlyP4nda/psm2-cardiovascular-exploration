using System;
using System.Collections;
using System.Collections.Generic;
using Cardio.Backend;
using Cardio.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// A scripted transport, so offline behaviour can be tested without a network.
    ///
    /// Nobody unplugs their router while finishing a level, which is exactly why
    /// the queue is the part of Phase 7 most likely to be wrong and least likely
    /// to be caught by hand. This lets a test be offline on demand, come back,
    /// and go away again mid-flush.
    /// </summary>
    public class FakeTransport : ISupabaseTransport
    {
        public bool Online = true;
        public long StatusCode = 201;
        public string Body = "";
        public int SendCount;
        public readonly List<string> SentBodies = new List<string>();

        /// <summary>Optional hook to change behaviour partway through a flush.</summary>
        public Action<int> BeforeEachSend;

        public IEnumerator Send(string method, string url, string jsonBody,
                                IDictionary<string, string> headers, int timeoutSeconds,
                                Action<BackendResponse> onComplete)
        {
            BeforeEachSend?.Invoke(SendCount);
            SendCount++;
            SentBodies.Add(jsonBody ?? string.Empty);

            yield return null;

            onComplete?.Invoke(Online
                ? new BackendResponse(StatusCode, Body, null, false)
                : BackendResponse.Offline("test: offline"));
        }
    }

    /// <summary>
    /// Phase 7: Supabase sync, the offline queue, and reconnection.
    ///
    /// Everything here runs against <see cref="FakeTransport"/>. That is
    /// deliberate: these tests assert the *queue logic*, which is what PSM1's
    /// NFR4 actually requires and what would silently lose a participant's data
    /// if it were wrong. They prove nothing about whether the live Supabase
    /// project accepts the payload — that needs the real server and is recorded
    /// as MANUAL REQUIRED in TESTING.md.
    /// </summary>
    public class SupabaseSyncTests
    {
        private string _backup;
        private FakeTransport _transport;
        private SupabaseConfig _config;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();

            SaveManager save = GameManager.Instance.Save;
            if (System.IO.File.Exists(save.SavePath)) _backup = System.IO.File.ReadAllText(save.SavePath);
            save.ResetProgress();

            _config = ScriptableObject.CreateInstance<SupabaseConfig>();
            _config.ProjectUrl = "https://example.supabase.co";
            _config.AnonKey = "sb_publishable_test";
            _config.SyncEnabled = true;
            _config.TimeoutSeconds = 5;

            _transport = new FakeTransport();

            SupabaseManager.Instance.SetConfig(_config);
            SupabaseManager.Instance.SetTransport(_transport);

            // Sign in through the fake rather than stubbing the auth state.
            // The flush deliberately refuses to upload while signed out - RLS
            // would reject it anyway - so a test that skipped this would be
            // testing nothing, which is exactly what the first run of these
            // tests did.
            yield return SignInThroughFake();
        }

        /// <summary>Drives the real sign-in path against a scripted auth response.</summary>
        private IEnumerator SignInThroughFake()
        {
            _transport.StatusCode = 200;
            _transport.Body = "{\"access_token\":\"test-access-token\"," +
                              "\"refresh_token\":\"test-refresh-token\"," +
                              "\"user\":{\"id\":\"11111111-2222-3333-4444-555555555555\",\"is_anonymous\":true}}";

            yield return AuthenticationManager.Instance.SignIn();

            Assert.IsTrue(AuthenticationManager.Instance.IsSignedIn,
                          "the fake sign-in should have succeeded: " + AuthenticationManager.Instance.LastError);

            // Reset for the row uploads that follow.
            _transport.StatusCode = 201;
            _transport.Body = string.Empty;
            _transport.SendCount = 0;
            _transport.SentBodies.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;
            if (save != null)
            {
                if (_backup != null) System.IO.File.WriteAllText(save.SavePath, _backup);
                else if (System.IO.File.Exists(save.SavePath)) System.IO.File.Delete(save.SavePath);
            }

            if (_config != null) UnityEngine.Object.DestroyImmediate(_config);
            yield return null;
        }

        // ------------------------------------------------------------------
        // Payload and schema mapping
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Payload_UsesTheSchemaColumnNames()
        {
            string json = SessionLogManager.Instance.BuildPayload(new SessionRecord
            {
                Level = 2, HintsUsed = 3, LevelFailures = 1,
                FinalDifficulty = 1, AverageResponseSeconds = 12.5f,
                PuzzlesAttempted = 4, PuzzlesCorrect = 3,
                DateUtc = "2026-08-31 10:00"
            });

            foreach (string column in new[]
                     {
                         "user_id", "current_level", "average_accuracy", "avg_response_time",
                         "final_difficulty_tier", "hints_used", "failed_attempts", "session_date"
                     })
            {
                StringAssert.Contains($"\"{column}\"", json, $"payload is missing the {column} column");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Payload_MapsFailedAttemptsToLevelFailures_NotIncorrectAnswers()
        {
            // The single mapping most likely to be got wrong, and the one that
            // would quietly corrupt the evaluation data if it were.
            // PSM1's FailedAttempts means "Blood Count hit zero".
            string json = SessionLogManager.Instance.BuildPayload(new SessionRecord
            {
                Level = 1,
                LevelFailures = 2,      // <- this is FailedAttempts
                IncorrectAnswers = 9    // <- this is NOT
            });

            StringAssert.Contains("\"failed_attempts\":2", json,
                                  "failed_attempts must carry LevelFailures");
            Assert.IsFalse(json.Contains("\"failed_attempts\":9"),
                           "failed_attempts must never carry IncorrectAnswers");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Payload_WritesTheDifficultyTierByName()
        {
            string easy = SessionLogManager.Instance.BuildPayload(new SessionRecord { FinalDifficulty = 0 });
            string hard = SessionLogManager.Instance.BuildPayload(new SessionRecord { FinalDifficulty = 2 });

            StringAssert.Contains("\"final_difficulty_tier\":\"Easy\"", easy);
            StringAssert.Contains("\"final_difficulty_tier\":\"Hard\"", hard);
            yield return null;
        }

        // ------------------------------------------------------------------
        // Offline queueing
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator WhenOffline_TheAttemptIsQueuedAndPlayContinues()
        {
            _transport.Online = false;
            SaveManager save = GameManager.Instance.Save;

            SessionLogManager.Instance.Enqueue("{\"user_id\":\"u\",\"current_level\":1}");
            yield return SessionLogManager.Instance.FlushQueue();

            Assert.AreEqual(1, save.Progress.PendingSessionLogs.Count,
                            "an attempt that could not be uploaded must stay queued");
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State,
                            "a failed upload must not interrupt play (PSM1 NFR4)");
        }

        [UnityTest]
        public IEnumerator AQueuedAttempt_SurvivesSaveAndReload()
        {
            SaveManager save = GameManager.Instance.Save;

            SessionLogManager.Instance.Enqueue("{\"user_id\":\"u\",\"current_level\":3}");
            save.Load();

            Assert.AreEqual(1, save.Progress.PendingSessionLogs.Count,
                            "the queue must survive a restart, or an offline session is lost");
            StringAssert.Contains("current_level\":3", save.Progress.PendingSessionLogs[0]);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheQueueIsNeverTheDashboardHistory()
        {
            SaveManager save = GameManager.Instance.Save;

            save.AppendSessionRecord(new SessionRecord { Level = 1, Score = 100 });
            yield return null;

            int historyBefore = save.Progress.SessionHistory.Count;
            Assert.AreEqual(1, historyBefore);

            // Draining the upload queue must leave the dashboard untouched.
            _transport.Online = true;
            yield return SessionLogManager.Instance.FlushQueue();

            Assert.IsEmpty(save.Progress.PendingSessionLogs, "the queue should have drained");
            Assert.AreEqual(historyBefore, save.Progress.SessionHistory.Count,
                            "a successful sync must never remove the player's own history");
        }

        // ------------------------------------------------------------------
        // Reconnection
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator WhenConnectivityReturns_TheQueueFlushesInOrder()
        {
            SaveManager save = GameManager.Instance.Save;
            _transport.Online = false;

            for (int i = 1; i <= 3; i++)
            {
                SessionLogManager.Instance.Enqueue($"{{\"user_id\":\"u\",\"current_level\":{i}}}");
            }
            yield return SessionLogManager.Instance.FlushQueue();
            Assert.AreEqual(3, save.Progress.PendingSessionLogs.Count, "all three should be waiting");

            // The offline flush legitimately makes one attempt - that is how it
            // discovers it is offline - so only what follows the reconnect is
            // interesting here.
            Assert.AreEqual(1, _transport.SentBodies.Count,
                            "an offline flush should try once and then stop, not spin through the queue");
            _transport.SentBodies.Clear();

            _transport.Online = true;
            yield return SessionLogManager.Instance.FlushQueue();

            Assert.IsEmpty(save.Progress.PendingSessionLogs, "reconnecting should drain the queue");
            Assert.AreEqual(3, _transport.SentBodies.Count, "each queued row should be sent exactly once");
            StringAssert.Contains("current_level\":1", _transport.SentBodies[0], "oldest must go first");
            StringAssert.Contains("current_level\":3", _transport.SentBodies[2], "newest must go last");
        }

        [UnityTest]
        public IEnumerator IfTheConnectionDropsMidFlush_TheRestStayQueued()
        {
            SaveManager save = GameManager.Instance.Save;

            for (int i = 1; i <= 4; i++)
            {
                SessionLogManager.Instance.Enqueue($"{{\"user_id\":\"u\",\"current_level\":{i}}}");
            }

            // Two succeed, then the network goes away again.
            _transport.Online = true;
            _transport.BeforeEachSend = sent => { if (sent >= 2) _transport.Online = false; };

            yield return SessionLogManager.Instance.FlushQueue();

            Assert.AreEqual(2, save.Progress.PendingSessionLogs.Count,
                            "the two that were not sent must remain, and no more");
            StringAssert.Contains("current_level\":3", save.Progress.PendingSessionLogs[0],
                                  "the queue must resume where it stopped, not restart");
        }

        [UnityTest]
        public IEnumerator ARowTheServerRejects_IsDroppedRatherThanBlockingTheQueue()
        {
            SaveManager save = GameManager.Instance.Save;

            SessionLogManager.Instance.Enqueue("{\"malformed\":true}");
            SessionLogManager.Instance.Enqueue("{\"user_id\":\"u\",\"current_level\":2}");

            _transport.Online = true;
            _transport.StatusCode = 400;
            _transport.Body = "{\"message\":\"bad request\"}";

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Dropping a session log"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Dropping a session log"));

            yield return SessionLogManager.Instance.FlushQueue();

            Assert.IsEmpty(save.Progress.PendingSessionLogs,
                           "a permanently rejected row must not block everything behind it forever");
        }

        // ------------------------------------------------------------------
        // Safety
        // ------------------------------------------------------------------

        [Test]
        public void AnonKeyIsAccepted_AndAServiceRoleKeyIsRefused()
        {
            var cfg = ScriptableObject.CreateInstance<SupabaseConfig>();
            try
            {
                cfg.ProjectUrl = "https://example.supabase.co";

                // role: anon
                cfg.AnonKey = "eyJhbGciOiJIUzI1NiJ9." +
                              Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"role\":\"anon\"}"))
                                     .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";
                Assert.IsTrue(cfg.KeyLooksLikeAnonKey(out _), "an anon key should be accepted");

                // role: service_role - bypasses RLS, must never ship
                cfg.AnonKey = "eyJhbGciOiJIUzI1NiJ9." +
                              Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"role\":\"service_role\"}"))
                                     .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";
                Assert.IsFalse(cfg.KeyLooksLikeAnonKey(out string detail),
                               "a service_role key must be refused");
                StringAssert.Contains("SERVICE_ROLE", detail);

                cfg.AnonKey = "sb_secret_abc123";
                Assert.IsFalse(cfg.KeyLooksLikeAnonKey(out _), "a secret key must be refused");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void TheShippedConfigCarriesAnAnonKey()
        {
            // Guards the real asset, not a fixture: if someone pastes a
            // service_role key into the committed config, this fails.
            var shipped = Resources.Load<SupabaseConfig>("Supabase/SupabaseConfig");
            Assert.IsNotNull(shipped, "the generated Supabase config is missing");
            Assert.IsTrue(shipped.IsConfigured, "the shipped config has no URL or key");
            Assert.IsTrue(shipped.KeyLooksLikeAnonKey(out string detail),
                          $"the SHIPPED config key is not an anon key: {detail}");
        }

        [Test]
        public void AProjectUrlPastedWithItsRestPath_IsStillUsable()
        {
            // The dashboard shows the URL in more than one form and the REST
            // path is easy to paste along with it.
            var cfg = ScriptableObject.CreateInstance<SupabaseConfig>();
            try
            {
                cfg.ProjectUrl = "https://example.supabase.co/rest/v1/";
                Assert.AreEqual("https://example.supabase.co/rest/v1", cfg.RestUrl);
                Assert.AreEqual("https://example.supabase.co/auth/v1", cfg.AuthUrl);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cfg);
            }
        }

        // ------------------------------------------------------------------
        // Behaviour under load: what a busy or unhappy server must not cost us
        //
        // These matter most in exactly the situation the project is built for -
        // a cohort playing at once behind one campus address. Under that load
        // Supabase answers with 429 and the occasional 5xx, and every one of
        // those used to be treated as "this row is bad" and deleted.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator A429_KeepsTheRowQueued_RatherThanDeletingIt()
        {
            SaveManager save = GameManager.Instance.Save;
            SessionLogManager logs = SessionLogManager.Instance;

            logs.Enqueue(logs.BuildPayload(new SessionRecord { Level = 1, Score = 50 }));

            _transport.StatusCode = 429;
            _transport.Body = "{\"message\":\"rate limit exceeded\"}";

            yield return logs.FlushQueue();

            Assert.AreEqual(1, save.Progress.PendingSessionLogs.Count,
                            "a rate-limited row must stay queued, not be thrown away");
            StringAssert.Contains("429", logs.LastStatus);

            // ...and must still go up once the server calms down.
            _transport.StatusCode = 201;
            _transport.Body = string.Empty;
            yield return logs.FlushQueue();

            Assert.IsEmpty(save.Progress.PendingSessionLogs, "the row should upload once the server recovers");
        }

        [UnityTest]
        public IEnumerator AServerError_KeepsTheRowQueued()
        {
            SaveManager save = GameManager.Instance.Save;
            SessionLogManager logs = SessionLogManager.Instance;

            logs.Enqueue(logs.BuildPayload(new SessionRecord { Level = 2 }));

            _transport.StatusCode = 503;
            yield return logs.FlushQueue();

            Assert.AreEqual(1, save.Progress.PendingSessionLogs.Count,
                            "a 5xx is the server's problem, not the row's");
        }

        [UnityTest]
        public IEnumerator AMalformedRow_IsStillDropped_SoItCannotBlockTheQueue()
        {
            SaveManager save = GameManager.Instance.Save;
            SessionLogManager logs = SessionLogManager.Instance;

            logs.Enqueue(logs.BuildPayload(new SessionRecord { Level = 1 }));

            _transport.StatusCode = 400;
            _transport.Body = "{\"message\":\"malformed\"}";

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Dropping a session log"));
            yield return logs.FlushQueue();

            Assert.IsEmpty(save.Progress.PendingSessionLogs,
                           "a row the server genuinely refuses must not block everything behind it");
        }

        [UnityTest]
        public IEnumerator AnExpiredToken_IsRefreshed_AndTheRowStillArrives()
        {
            SaveManager save = GameManager.Instance.Save;
            SessionLogManager logs = SessionLogManager.Instance;

            logs.Enqueue(logs.BuildPayload(new SessionRecord { Level = 3 }));

            // send 0: the upload, rejected because the JWT has expired
            // send 1: the token refresh
            // send 2: the same row again, now accepted
            _transport.BeforeEachSend = index =>
            {
                if (index == 0)
                {
                    _transport.StatusCode = 401;
                    _transport.Body = "{\"message\":\"JWT expired\"}";
                }
                else if (index == 1)
                {
                    _transport.StatusCode = 200;
                    _transport.Body = "{\"access_token\":\"fresh-token\"," +
                                      "\"refresh_token\":\"fresh-refresh\"," +
                                      "\"user\":{\"id\":\"11111111-2222-3333-4444-555555555555\"}}";
                }
                else
                {
                    _transport.StatusCode = 201;
                    _transport.Body = string.Empty;
                }
            };

            yield return logs.FlushQueue();
            _transport.BeforeEachSend = null;

            Assert.IsEmpty(save.Progress.PendingSessionLogs,
                           "an expired token should cost a refresh, not the attempt: " + logs.LastStatus);
            Assert.AreEqual("fresh-token", AuthenticationManager.Instance.AccessToken,
                            "the refreshed token should be the one in use");
        }

        [UnityTest]
        public IEnumerator ARowQueuedBeforeSignIn_IsStampedWithTheRealUserId()
        {
            SaveManager save = GameManager.Instance.Save;
            SessionLogManager logs = SessionLogManager.Instance;

            // A row built with no identity available - what happens when a level
            // finishes before the staggered sign-in has come back.
            save.Progress.PendingSessionLogs.Add(
                "{\"user_id\":\"\",\"current_level\":1,\"hints_used\":0}");
            save.SaveNow();

            yield return logs.FlushQueue();

            Assert.IsEmpty(save.Progress.PendingSessionLogs, "the row should have uploaded");
            Assert.AreEqual(1, _transport.SentBodies.Count);
            StringAssert.Contains("\"user_id\":\"11111111-2222-3333-4444-555555555555\"",
                                  _transport.SentBodies[0],
                                  "the identity must be stamped on at send time, not left empty from enqueue time");
        }

        [Test]
        public void StampUserId_RewritesOnlyTheIdentity()
        {
            const string payload = "{\"user_id\":\"\",\"current_level\":2,\"hints_used\":5}";

            string stamped = SessionLogManager.StampUserId(payload, "abc-123");

            Assert.AreEqual("{\"user_id\":\"abc-123\",\"current_level\":2,\"hints_used\":5}", stamped);
            Assert.AreEqual(payload, SessionLogManager.StampUserId(payload, null),
                            "no identity to stamp means the payload is left alone");
        }

        [Test]
        public void TheUploadQueue_IsBounded()
        {
            SaveManager save = GameManager.Instance.Save;
            SessionLogManager logs = SessionLogManager.Instance;

            // Filled directly rather than through Enqueue: the point is the cap,
            // not two hundred file writes.
            for (int i = 0; i < SaveManager.MaxPendingSessionLogs; i++)
            {
                save.Progress.PendingSessionLogs.Add("{\"user_id\":\"x\",\"n\":" + i + "}");
            }

            logs.Enqueue("{\"user_id\":\"x\",\"n\":\"newest\"}");

            Assert.AreEqual(SaveManager.MaxPendingSessionLogs, save.Progress.PendingSessionLogs.Count,
                            "the queue must not grow past its cap");
            Assert.IsFalse(save.Progress.PendingSessionLogs.Contains("{\"user_id\":\"x\",\"n\":0}"),
                           "the oldest row is the one to drop");
            Assert.IsTrue(save.Progress.PendingSessionLogs.Contains("{\"user_id\":\"x\",\"n\":\"newest\"}"),
                          "the newest row must be kept");
        }


    }
}
