using System.Collections;
using Cardio.AI;
using Cardio.Core;
using Cardio.Data;
using Cardio.DDA;
using Cardio.Gameplay;
using Cardio.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// The wrong-answer combat loop:
    ///
    ///     wrong answer -> one leukemic blast tagged with that question
    ///     kill it      -> that question's hint, and a score cost
    ///     all answered -> a kill pays a bonus instead
    ///     all dead     -> they return after the respawn delay
    /// </summary>
    public class HostileCombatTests
    {
        private HostileSpawnDirector _director;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();
            TestLevel.EnableHostileSpawning();

            _director = Object.FindAnyObjectByType<HostileSpawnDirector>();
            Assert.IsNotNull(_director, "the level should contain a HostileSpawnDirector");
        }

        [TearDown]
        public void TearDown()
        {
            if (PuzzleManager.Instance != null && PuzzleManager.Instance.IsPuzzleActive)
            {
                PuzzleManager.Instance.AbandonPuzzle();
            }

            PlayerInputReader.ClearSource();
        }

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AWrongAnswer_SpawnsOneBlastTaggedWithThatQuestion()
        {
            PuzzleData puzzle = OpenAnyPuzzle();
            Assert.AreEqual(0, _director.AliveCount, "no hostiles before the first mistake");

            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);

            Assert.AreEqual(1, _director.AliveCount, "a wrong answer should spawn exactly one blast");

            LeukemicBlastAgent blast = _director.BlastFor(puzzle.PuzzleId);
            Assert.IsNotNull(blast, "the blast should be registered against the question that spawned it");
            Assert.AreEqual(puzzle.PuzzleId, blast.PuzzleId);
            Assert.IsTrue(blast.IsAlive);

            // And the metric is recorded.
            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            Assert.AreEqual(1, record.TotalHostilesSpawned);
        }

        [UnityTest]
        public IEnumerator RepeatedWrongAnswers_DoNotStackBlastsOnTheSameQuestion()
        {
            PuzzleData puzzle = OpenAnyPuzzle();

            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);
            Assert.AreEqual(1, _director.AliveCount);

            // Two more mistakes on the same question.
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);

            Assert.AreEqual(1, _director.AliveCount,
                            "one blast per question - stacking would punish exactly the player who is struggling");

            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            Assert.AreEqual(1, record.TotalHostilesSpawned, "only the first mistake should have spawned anything");
        }

        [UnityTest]
        public IEnumerator WhenTheTierDisablesSpawning_NoBlastAppears()
        {
            // Hard offers no automatic help and no combat route to it either.
            DDAManager.Instance.ForceTier(DifficultyTier.Hard, "test");
            yield return null;

            Assert.IsFalse(PuzzleManager.Instance.HostileSpawningEnabled,
                           "Hard should switch wrong-answer spawning off");

            PuzzleData puzzle = OpenAnyPuzzle();
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);

            Assert.AreEqual(0, _director.AliveCount, "no blast should spawn while the tier forbids it");
        }

        // ------------------------------------------------------------------
        // Killing
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator KillingABlast_DeliversThatQuestionsHint_AndCostsScore()
        {
            PuzzleData puzzle = OpenAnyPuzzle();
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);

            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            record.Score = 100;                       // a known starting point
            GameManager.Instance.Session.Score = 100;

            int earnedBefore = record.EarnedHints;

            LeukemicBlastAgent blast = _director.BlastFor(puzzle.PuzzleId);
            blast.Health.TakeDamage(blast.Health.MaxHealth);
            yield return TestLevel.Frames(2);

            Assert.IsFalse(blast.IsAlive);
            Assert.AreEqual(0, _director.AliveCount);
            Assert.AreEqual(1, record.TotalHostilesKilled);

            // The hint for THAT question, not some other one.
            Assert.IsTrue(PuzzleManager.Instance.HasBankedHint(puzzle.PuzzleId),
                          "the kill should bank the hint for the question that spawned it");
            Assert.AreEqual(earnedBefore + 1, record.EarnedHints, "an earned hint should be counted separately");
            Assert.AreEqual(0, record.HintsUsed, "an earned hint is not a requested one");

            Assert.AreEqual(90, record.Score, "a kill should cost 10 points while questions remain");
        }

        [UnityTest]
        public IEnumerator ABankedHint_IsAlreadyShowingWhenThatPuzzleIsOpened()
        {
            PuzzleData puzzle = OpenAnyPuzzle();
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);
            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            _director.BlastFor(puzzle.PuzzleId).Health.TakeDamage(999);
            yield return TestLevel.Frames(2);

            Assert.IsTrue(PuzzleManager.Instance.HasBankedHint(puzzle.PuzzleId));

            // Re-open the same question: the hint must already be on screen,
            // because one seen minutes ago and forgotten is worth nothing.
            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle));
            yield return TestLevel.Frames(2);

            var feedback = TestLevel.Find("Feedback").GetComponent<TMPro.TMP_Text>();
            StringAssert.Contains(puzzle.Hint, feedback.text, "the earned hint should be pre-revealed");

            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            Assert.AreEqual(1, record.EarnedHints, "re-opening must not count the hint a second time");
        }

        [UnityTest]
        public IEnumerator OnceEveryQuestionIsAnswered_AKillPaysABonusInsteadOfAHint()
        {
            PuzzleData puzzle = OpenAnyPuzzle();
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);
            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            // Satisfy every puzzle objective so there is nothing left to hint at.
            ObjectiveManager objectives = ObjectiveManager.Instance;
            for (int i = 0; i < objectives.Objectives.Count; i++)
            {
                if (objectives.Objectives[i].Kind != ObjectiveKind.ReachExit) objectives.CompleteObjective(i);
            }
            yield return null;
            Assert.IsTrue(objectives.AllNonExitObjectivesComplete());

            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            record.Score = 100;
            GameManager.Instance.Session.Score = 100;
            int earnedBefore = record.EarnedHints;

            _director.BlastFor(puzzle.PuzzleId).Health.TakeDamage(999);
            yield return TestLevel.Frames(2);

            Assert.AreEqual(125, record.Score, "a kill after every question is answered should pay +25");
            Assert.AreEqual(earnedBefore, record.EarnedHints, "there is nothing left to hint about");
        }

        // ------------------------------------------------------------------
        // Respawn
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator WhenEveryBlastIsDead_TheyReturnAfterTheDelay()
        {
            PuzzleData puzzle = OpenAnyPuzzle();
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);
            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            LeukemicBlastAgent blast = _director.BlastFor(puzzle.PuzzleId);
            Vector3 spawnPoint = blast.SpawnPosition;

            blast.Health.TakeDamage(999);
            yield return TestLevel.Frames(2);

            Assert.AreEqual(0, _director.AliveCount);
            Assert.Greater(_director.RespawnCountdown, 0f, "clearing every blast should start the respawn timer");

            // Skip the 30-second wait rather than sitting through it.
            _director.ForceRespawnNow();
            yield return TestLevel.Frames(2);

            Assert.AreEqual(1, _director.AliveCount, "the blast should return");
            Assert.IsTrue(blast.IsAlive);
            Assert.AreEqual(blast.Health.MaxHealth, blast.Health.CurrentHealth, "it should return at full health");
            Assert.Less(Vector3.Distance(blast.transform.position, spawnPoint), 2f,
                        "it should return to where it first appeared");
        }

        // ------------------------------------------------------------------
        // The attack itself
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheAttack_DamagesBlasts_ButNotHealthyImmuneCells()
        {
            var attack = TestLevel.Player.GetComponent<PlayerAttack>();
            Assert.IsNotNull(attack, "the player prefab should carry PlayerAttack");

            PuzzleData puzzle = OpenAnyPuzzle();
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);
            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            LeukemicBlastAgent blast = _director.BlastFor(puzzle.PuzzleId);

            // Put the blast directly in front of the player. MoveTo, not a raw
            // transform write - the CharacterController would undo that.
            TestLevel.Player.Teleport(new Vector3(0f, 1.2f, -6f), Quaternion.identity);
            yield return TestLevel.Frames(2);
            blast.MoveTo(TestLevel.Player.transform.position + TestLevel.Player.transform.forward * 1.3f);
            yield return TestLevel.Frames(2);

            int before = blast.Health.CurrentHealth;
            Assert.Greater(attack.TrySwing(), 0, "the swing should connect with the blast");
            Assert.Less(blast.Health.CurrentHealth, before, "the blast should lose health");

            // A healthy immune cell has no NpcHealth, so it cannot be harmed.
            // Blasts reuse ObstacleAgent for chasing, so they must be excluded -
            // which is the same distinction ObstacleManager.Rescan makes.
            ObstacleAgent healthy = null;
            foreach (ObstacleAgent candidate in Object.FindObjectsByType<ObstacleAgent>(FindObjectsInactive.Exclude))
            {
                if (candidate.GetComponent<LeukemicBlastAgent>() != null) continue;
                healthy = candidate;
                break;
            }

            Assert.IsNotNull(healthy, "Level 1 should still contain healthy immune cells");
            Assert.IsNull(healthy.GetComponent<NpcHealth>(),
                          "neutrophils and monocytes must stay unkillable hazards, not enemies");

            // And the registry must not have absorbed the blast.
            ObstacleManager.Instance.Rescan();
            foreach (ObstacleAgent registered in ObstacleManager.Instance.Agents)
            {
                Assert.IsNull(registered.GetComponent<LeukemicBlastAgent>(),
                              "ObstacleManager should track only healthy immune cells");
            }
        }

        [UnityTest]
        public IEnumerator ThreeSwings_DestroyABlast()
        {
            var attack = TestLevel.Player.GetComponent<PlayerAttack>();

            PuzzleData puzzle = OpenAnyPuzzle();
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);
            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            LeukemicBlastAgent blast = _director.BlastFor(puzzle.PuzzleId);
            TestLevel.Player.Teleport(new Vector3(0f, 1.2f, -6f), Quaternion.identity);
            yield return TestLevel.Frames(2);

            for (int i = 0; i < 3 && blast.IsAlive; i++)
            {
                blast.MoveTo(TestLevel.Player.transform.position + TestLevel.Player.transform.forward * 1.3f);
                yield return TestLevel.Frames(2);

                attack.TrySwing();
                yield return new WaitForSeconds(0.6f);   // clear the cooldown
            }

            Assert.IsFalse(blast.IsAlive, "three swings at 34 damage should finish a 100-health blast");
            Assert.AreEqual(1, attack.Kills, "the kill should be attributed to the player");
        }

        [UnityTest]
        public IEnumerator AnsweringWrongAfterAKill_RevivesTheSameBlast_RatherThanLeakingAReplacement()
        {
            // A real sequence: answer wrong, kill the blast to earn its hint, then
            // get the same question wrong again. The early return in TrySpawnFor only
            // covered a *live* blast, so this path used to instantiate a replacement
            // and orphan the dead one - still parented, still subscribed to its own
            // Died event, and unreachable by RespawnAll, which only walks the
            // dictionary. Nothing counted it, so nothing noticed.
            PuzzleData puzzle = OpenAnyPuzzle();

            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);

            LeukemicBlastAgent first = _director.BlastFor(puzzle.PuzzleId);
            Assert.IsNotNull(first, "the first wrong answer should have spawned a blast");
            Assert.AreEqual(1, CountBlastObjects(), "exactly one blast object should exist");

            first.Health.TakeDamage(999);
            yield return TestLevel.Frames(2);
            Assert.IsFalse(first.IsAlive, "the blast should be dead");
            Assert.AreEqual(0, _director.AliveCount);

            // Same question, wrong again. No need to reopen: one wrong answer does
            // not resolve a puzzle - the attempt allowance is three - so the panel is
            // still taking answers.
            Assert.IsTrue(PuzzleManager.Instance.IsAcceptingAnswers,
                          "the puzzle should still be open after a single wrong answer");
            SubmitWrong(puzzle);
            yield return TestLevel.Frames(2);

            LeukemicBlastAgent second = _director.BlastFor(puzzle.PuzzleId);
            Assert.IsNotNull(second, "a blast should be present again");
            Assert.IsTrue(second.IsAlive, "the revived blast should be alive");
            Assert.AreSame(first, second, "the dead blast should be revived, not replaced");

            // The assertion that actually catches the leak: inactive objects count too.
            Assert.AreEqual(1, CountBlastObjects(),
                            "reviving must not leave an orphaned blast object behind");
        }

        /// <summary>
        /// Every blast object in the scene, including deactivated ones.
        ///
        /// FindObjectsInactive.Include is the point: a killed blast is only
        /// SetActive(false), so a leaked one is invisible to any count that skips
        /// inactive objects - which is why nothing caught this.
        /// </summary>
        private static int CountBlastObjects()
        {
            return Object.FindObjectsByType<LeukemicBlastAgent>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static PuzzleData OpenAnyPuzzle()
        {
            foreach (PuzzleData puzzle in PuzzleManager.Instance.Bank.Puzzles)
            {
                if (puzzle == null) continue;
                if (puzzle.Complexity > PuzzleManager.Instance.MaxComplexity) continue;
                if (PuzzleManager.Instance.IsSolved(puzzle.PuzzleId)) continue;

                Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle));
                return puzzle;
            }

            Assert.Fail("no puzzle available at the current tier");
            return null;
        }

        private static void SubmitWrong(PuzzleData puzzle)
        {
            switch (puzzle.Type)
            {
                case PuzzleType.MultipleChoice:
                    PuzzleManager.Instance.SubmitOption((puzzle.CorrectOptionIndex + 1) % puzzle.Options.Length);
                    break;
                case PuzzleType.BloodFlowSequence:
                    var reversed = new System.Collections.Generic.List<string>(puzzle.SequenceSteps);
                    reversed.Reverse();
                    PuzzleManager.Instance.SubmitSequence(reversed);
                    break;
                default:
                    PuzzleManager.Instance.SubmitStructure("not_a_real_structure");
                    break;
            }
        }
    }
}
