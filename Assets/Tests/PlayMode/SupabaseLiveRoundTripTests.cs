using System.Collections;
using Cardio.Backend;
using Cardio.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// The live round-trip: real managers, real transport, real Supabase project.
    ///
    /// EXPLICIT ON PURPOSE - this does not run in the normal suite. Two reasons,
    /// both practical rather than stylistic:
    ///
    ///  * it needs the internet, so including it would make a deterministic
    ///    suite fail for reasons that have nothing to do with the code;
    ///  * Supabase rate-limits anonymous sign-ins to 30 per hour per IP, and a
    ///    suite that signs in on every run would burn that allowance during
    ///    ordinary development and then start failing.
    ///
    /// Run it deliberately, when verifying the backend actually works:
    ///
    ///   Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode \
    ///     -testFilter "SupabaseLiveRoundTripTests" -testResults live.xml
    ///
    /// What it proves that <see cref="SupabaseSyncTests"/> cannot: that the
    /// payload this game actually builds is accepted by the real table, under
    /// the real RLS policies, using the real HTTP stack. The fake-transport
    /// suite proves the queue logic; this proves the contract.
    /// </summary>
    [Explicit("Hits the live Supabase project and consumes the anonymous sign-in rate limit.")]
    public class SupabaseLiveRoundTripTests
    {
        private string _backup;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();

            SaveManager save = GameManager.Instance.Save;
            if (System.IO.File.Exists(save.SavePath)) _backup = System.IO.File.ReadAllText(save.SavePath);

            // Force the production transport: TestLevel.Load may have left a
            // fake in place from another suite.
            SupabaseManager.Instance.SetTransport(new UnityWebRequestTransport());
            SupabaseManager.Instance.SetConfig(Resources.Load<SupabaseConfig>("Supabase/SupabaseConfig"));
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
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheGameCanSignIn_UploadARow_AndReadItBack()
        {
            SupabaseManager backend = SupabaseManager.Instance;
            Assert.IsTrue(backend.IsEnabled, "the shipped config should be usable");

            // ---- Sign in, through the real AuthenticationManager ----
            AuthenticationManager auth = AuthenticationManager.Instance;
            yield return auth.SignIn();

            Assert.IsTrue(auth.IsSignedIn,
                          "live anonymous sign-in failed: " + auth.LastError);
            Assert.IsNotEmpty(auth.UserId, "a signed-in user must have an id");
            Debug.Log($"[LiveTest] signed in as {auth.UserId}");

            // ---- Queue a row and flush it, through the real SessionLogManager ----
            SaveManager save = GameManager.Instance.Save;
            save.Progress.PendingSessionLogs.Clear();

            var record = new SessionRecord
            {
                DateUtc = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Level = 2,
                HintsUsed = 5,
                LevelFailures = 3,      // -> failed_attempts
                IncorrectAnswers = 11,  // -> must NOT appear
                FinalDifficulty = 2,    // -> "Hard"
                AverageResponseSeconds = 9.25f,
                PuzzlesAttempted = 4,
                PuzzlesCorrect = 3,
                Completed = true
            };

            string payload = SessionLogManager.Instance.BuildPayload(record);
            Debug.Log($"[LiveTest] payload: {payload}");

            SessionLogManager.Instance.Enqueue(payload);
            yield return SessionLogManager.Instance.FlushQueue();

            // Signing in also starts a flush, so this call may have handed the
            // work to one already running rather than doing it itself. Wait for
            // the queue to actually drain instead of assuming which coroutine
            // did the draining.
            yield return TestLevel.WaitUntil(
                () => save.Progress.PendingSessionLogs.Count == 0, 30f,
                "the live queue to drain - status: " + SessionLogManager.Instance.LastStatus);

            Assert.IsEmpty(save.Progress.PendingSessionLogs,
                           "the live flush should have drained the queue - status: " +
                           SessionLogManager.Instance.LastStatus);

            // ---- Read it back, so "it uploaded" is not taken on trust ----
            string url = $"{backend.Config.RestUrl}/session_logs" +
                         $"?select=current_level,hints_used,failed_attempts,final_difficulty_tier" +
                         $"&order=session_date.desc&limit=1";

            BackendResponse read = default;
            yield return backend.Send("GET", url, null, backend.RestHeaders(auth.AccessToken), r => read = r);

            Assert.IsTrue(read.Success, $"read-back failed ({read.StatusCode}): {read.Error}");
            Debug.Log($"[LiveTest] read back: {read.Body}");

            StringAssert.Contains("\"current_level\":2", read.Body, "the row we just wrote should come back");
            StringAssert.Contains("\"hints_used\":5", read.Body);
            StringAssert.Contains("\"final_difficulty_tier\":\"Hard\"", read.Body);

            // The mapping that matters most: FailedAttempts is LevelFailures (3),
            // never IncorrectAnswers (11).
            StringAssert.Contains("\"failed_attempts\":3", read.Body,
                                  "failed_attempts must carry LevelFailures");
            Assert.IsFalse(read.Body.Contains("\"failed_attempts\":11"),
                           "failed_attempts must never carry IncorrectAnswers");
        }

        [UnityTest]
        public IEnumerator TheStoredSession_IsReusedRatherThanCreatingANewUser()
        {
            AuthenticationManager auth = AuthenticationManager.Instance;

            yield return auth.SignIn();
            Assert.IsTrue(auth.IsSignedIn, "first sign-in failed: " + auth.LastError);
            string firstId = auth.UserId;

            // Signing in again must refresh the stored session, not mint a new
            // anonymous user - otherwise every launch strands the last one's
            // rows under an identity nobody can return to.
            yield return auth.SignIn();
            Assert.IsTrue(auth.IsSignedIn, "second sign-in failed: " + auth.LastError);

            Assert.AreEqual(firstId, auth.UserId,
                            "the same install must keep the same anonymous identity across launches");
        }
    }
}
