using System.IO;
using Cardio.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// TC-06 persistence, automated.
    ///
    /// SaveManager writes real JSON to the real persistent data path, so these
    /// tests exercise the production code path including the corruption
    /// recovery that PSM1 NFR4 implies. The existing save file is backed up and
    /// restored so running the suite never destroys a developer's progress.
    /// </summary>
    public class SavePersistenceTests
    {
        private GameObject _host;
        private SaveManager _save;
        private string _backup;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("SaveManagerTestHost");

            // AddComponent runs Awake immediately, which loads any existing file.
            _save = _host.AddComponent<SaveManager>();

            if (File.Exists(_save.SavePath)) _backup = File.ReadAllText(_save.SavePath);

            _save.ResetProgress();
        }

        [TearDown]
        public void TearDown()
        {
            if (_backup != null) File.WriteAllText(_save.SavePath, _backup);
            else if (File.Exists(_save.SavePath)) File.Delete(_save.SavePath);

            Object.DestroyImmediate(_host);
        }

        [Test]
        public void FreshProgress_LocksLevelsTwoAndThree()
        {
            Assert.IsFalse(_save.HasProgress, "a fresh profile should not enable Continue");
            Assert.IsTrue(_save.IsLevelUnlocked(LevelId.Level1_LeftVentricle));
            Assert.IsFalse(_save.IsLevelUnlocked(LevelId.Level2_Brain));
            Assert.IsFalse(_save.IsLevelUnlocked(LevelId.Level3_RightVentricle));
        }

        [Test]
        public void CompletingLevelOne_UnlocksLevelTwo_AndPersists()
        {
            _save.MarkLevelCompleted(LevelId.Level1_LeftVentricle);

            Assert.IsTrue(_save.HasProgress);
            Assert.IsTrue(_save.IsLevelUnlocked(LevelId.Level2_Brain));
            Assert.IsFalse(_save.IsLevelUnlocked(LevelId.Level3_RightVentricle),
                           "finishing level 1 must not unlock level 3");

            // Round-trip through disk, exactly as a relaunch would.
            _save.Load();

            Assert.AreEqual(2, _save.Progress.HighestUnlockedLevel);
            CollectionAssert.Contains(_save.Progress.CompletedLevels, 1);
            Assert.AreEqual(LevelId.Level2_Brain, _save.ResumeLevel());
        }

        [Test]
        public void UnlockingNeverExceedsTheFinalLevel()
        {
            _save.MarkLevelCompleted(LevelId.Level1_LeftVentricle);
            _save.MarkLevelCompleted(LevelId.Level2_Brain);
            _save.MarkLevelCompleted(LevelId.Level3_RightVentricle);

            Assert.AreEqual(GameConstants.LevelScenes.Length, _save.Progress.HighestUnlockedLevel,
                            "unlock level should clamp at the last level");
            Assert.AreEqual(LevelId.Level3_RightVentricle, _save.ResumeLevel());
        }

        [Test]
        public void CompletingTheSameLevelTwice_DoesNotDuplicateIt()
        {
            _save.MarkLevelCompleted(LevelId.Level1_LeftVentricle);
            _save.MarkLevelCompleted(LevelId.Level1_LeftVentricle);

            Assert.AreEqual(1, _save.Progress.CompletedLevels.Count);
        }

        [Test]
        public void CorruptSaveFile_RecoversWithFreshProgress_AndDoesNotThrow()
        {
            _save.MarkLevelCompleted(LevelId.Level1_LeftVentricle);
            File.WriteAllText(_save.SavePath, "}{ this is not json at all ][");

            LogAssert.ignoreFailingMessages = true;   // a warning is expected and correct
            Assert.DoesNotThrow(() => _save.Load());
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(_save.HasProgress, "corrupt data should fall back to a fresh profile");
            Assert.IsNotNull(_save.Progress.CompletedLevels, "lists must never come back null");
            Assert.IsNotNull(_save.Progress.PendingSessionLogs);
        }

        [Test]
        public void MissingSaveFile_LoadsFreshWithoutThrowing()
        {
            if (File.Exists(_save.SavePath)) File.Delete(_save.SavePath);

            Assert.DoesNotThrow(() => _save.Load());
            Assert.IsFalse(_save.HasProgress);
        }

        [Test]
        public void ResetProgress_RelocksEverything()
        {
            _save.MarkLevelCompleted(LevelId.Level1_LeftVentricle);
            _save.MarkLevelCompleted(LevelId.Level2_Brain);

            _save.ResetProgress();
            _save.Load();

            Assert.IsFalse(_save.HasProgress);
            Assert.IsFalse(_save.IsLevelUnlocked(LevelId.Level2_Brain));
        }

        [Test]
        public void ProfileName_SurvivesARoundTrip()
        {
            // TC-09 step 3: the guest display name must persist across a relaunch.
            _save.Progress.LastUserId = "guest";
            _save.Progress.LastDisplayName = "Aisyah";
            _save.SaveNow();

            _save.Load();

            Assert.AreEqual("Aisyah", _save.Progress.LastDisplayName);
        }

        [Test]
        public void PendingSessionLogs_RoundTrip_ForPhase7OfflineQueue()
        {
            // PSM1 NFR4: session data must survive locally while Firebase is
            // unreachable. The queue is not consumed yet, but it must persist.
            _save.Progress.PendingSessionLogs.Add("{\"LogId\":\"abc\"}");
            _save.SaveNow();

            _save.Load();

            Assert.AreEqual(1, _save.Progress.PendingSessionLogs.Count);
            StringAssert.Contains("abc", _save.Progress.PendingSessionLogs[0]);
        }
    }
}
