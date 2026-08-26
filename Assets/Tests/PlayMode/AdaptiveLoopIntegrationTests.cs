using System.Collections;
using System.Collections.Generic;
using Cardio.AI;
using Cardio.Core;
using Cardio.Data;
using Cardio.DDA;
using Cardio.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// Phase 6: proves the whole PSM1 loop runs together in a live scene.
    ///
    /// The three headless self-checks each verify one layer in isolation -
    /// scoring arithmetic, difficulty policy, pathfinding. None of them proves
    /// the layers are actually *connected*: that answering a puzzle reaches the
    /// tracker, that the tracker reaches the DDA, and that a tier change reaches
    /// the puzzle system, the hint system, the hazards and the A* agents.
    ///
    /// These are PlayMode tests because that wiring only exists once Awake,
    /// Start and Update are running and a real level is loaded. They drive
    /// PuzzleManager through the same public API the UI uses, so the path under
    /// test is the production one.
    /// </summary>
    public class AdaptiveLoopIntegrationTests
    {
        private const float PuzzleCloseTimeout = 20f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // A fresh session first, so tier and metrics from a previous test do
            // not leak: the persistent managers survive scene loads by design.
            if (GameManager.Instance != null) GameManager.Instance.StartNewSession("integration_test", "Integration Test");

            yield return SceneManager.LoadSceneAsync(GameConstants.SceneLevel1);
            yield return WaitUntilLevelReady();
        }

        // ------------------------------------------------------------------
        // The loop, upwards
        // ------------------------------------------------------------------

        /// <summary>
        /// Answer easy puzzles quickly and correctly; the tier must rise and the
        /// change must be visible in every consumer.
        /// </summary>
        [UnityTest]
        public IEnumerator StrongPlay_PromotesTier_AndReachesEveryConsumer()
        {
            Assert.AreEqual(DifficultyTier.Easy, DDAManager.Instance.CurrentTier, "should open on Easy");

            // Snapshot what the consumers look like before adaptation.
            int complexityBefore = PuzzleManager.Instance.MaxComplexity;
            int attemptsBefore = PuzzleManager.Instance.MaxAttempts;
            float hazardBefore = DDAManager.Instance.HazardDamageMultiplier;
            float obstacleBefore = DDAManager.Instance.ObstacleSpeedMultiplier;

            PathfindingAgent agent = Object.FindAnyObjectByType<PathfindingAgent>();
            Assert.IsNotNull(agent, "Level 1 should contain at least one pathfinding agent");
            float agentSpeedBefore = agent.CurrentSpeed;

            // A complexity-2 puzzle must be refused while the cap is 1. This is
            // the strongest single proof that the DDA gates the puzzle system.
            PuzzleData harder = FindUnsolvedPuzzle(complexity: 2);
            Assert.IsNotNull(harder, "Level 1 bank should contain a complexity-2 puzzle");
            Assert.IsFalse(PuzzleManager.Instance.BeginPuzzle(harder),
                           "a complexity-2 puzzle must be refused while the Easy cap is 1");

            // Three fast, correct answers is enough: the policy needs 3 resolved
            // puzzles before it will act, and the cooldown is clear on level load.
            int answered = 0;
            for (int i = 0; i < 4 && DDAManager.Instance.CurrentTier == DifficultyTier.Easy; i++)
            {
                PuzzleData puzzle = FindUnsolvedPuzzle(complexity: 1);
                Assert.IsNotNull(puzzle, "ran out of complexity-1 puzzles before the tier moved");

                yield return AnswerPuzzle(puzzle, correctly: true);
                answered++;
            }

            // ---- The measurement layer saw it ----
            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            Assert.AreEqual(answered, record.PuzzlesAttempted, "tracker should have recorded every puzzle");
            Assert.AreEqual(answered, record.PuzzlesCorrect, "all answers were correct");
            Assert.Greater(record.Score, 0, "correct answers should score points");

            // ---- The policy acted ----
            Assert.AreEqual(DifficultyTier.Medium, DDAManager.Instance.CurrentTier,
                            "strong play should promote Easy -> Medium");
            Assert.AreEqual(DifficultyTier.Medium, GameManager.Instance.Session.CurrentDifficulty,
                            "the session (and therefore the HUD) should show the new tier");

            // ---- Every consumer received it ----
            Assert.Greater(PuzzleManager.Instance.MaxComplexity, complexityBefore,
                           "puzzle complexity cap should rise with the tier");
            Assert.Less(PuzzleManager.Instance.MaxAttempts, attemptsBefore,
                        "attempts allowed should tighten with the tier");
            Assert.Greater(DDAManager.Instance.HazardDamageMultiplier, hazardBefore,
                           "hazards should hurt more at a higher tier");
            Assert.Greater(DDAManager.Instance.ObstacleSpeedMultiplier, obstacleBefore,
                           "obstacle speed multiplier should rise with the tier");
            Assert.Greater(agent.CurrentSpeed, agentSpeedBefore,
                           "a live A* agent should actually move faster - this is the Phase 4 to 5 join");

            // ---- And the gate that was shut is now open ----
            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(harder),
                          "the same complexity-2 puzzle must now be accepted");
            PuzzleManager.Instance.AbandonPuzzle();
        }

        // ------------------------------------------------------------------
        // The loop, downwards
        // ------------------------------------------------------------------

        /// <summary>
        /// Repeated failure must lower the tier and soften the game, including
        /// the environment and the agents.
        /// </summary>
        [UnityTest]
        public IEnumerator RepeatedFailure_DemotesTier_AndSoftensTheGame()
        {
            DDAManager.Instance.ForceTier(DifficultyTier.Hard, "integration test setup");
            yield return null;

            Assert.AreEqual(DifficultyTier.Hard, DDAManager.Instance.CurrentTier);

            float hazardBefore = DDAManager.Instance.HazardDamageMultiplier;
            int attemptsBefore = PuzzleManager.Instance.MaxAttempts;

            PathfindingAgent agent = Object.FindAnyObjectByType<PathfindingAgent>();
            Assert.IsNotNull(agent);
            float agentSpeedBefore = agent.CurrentSpeed;

            // Three failed puzzles in a row trips the override, which fires even
            // during the change cooldown.
            for (int i = 0; i < 3; i++)
            {
                PuzzleData puzzle = FindUnsolvedPuzzle(complexity: -1);
                Assert.IsNotNull(puzzle, "ran out of puzzles to fail");

                yield return AnswerPuzzle(puzzle, correctly: false);
            }

            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            Assert.AreEqual(3, record.PuzzlesFailed, "three puzzles should be recorded as failed");
            Assert.Greater(record.IncorrectAnswers, 0, "wrong submissions should be counted individually");

            Assert.AreEqual(DifficultyTier.Medium, DDAManager.Instance.CurrentTier,
                            "three consecutive failures should demote Hard -> Medium");

            Assert.Less(DDAManager.Instance.HazardDamageMultiplier, hazardBefore,
                        "hazards should hurt less after a demotion");
            Assert.Greater(PuzzleManager.Instance.MaxAttempts, attemptsBefore,
                           "the player should get more attempts after a demotion");
            Assert.Less(agent.CurrentSpeed, agentSpeedBefore,
                        "agents should slow down after a demotion");
        }

        // ------------------------------------------------------------------
        // Navigation in a live scene
        // ------------------------------------------------------------------

        /// <summary>
        /// The grid builds from real geometry at runtime and agents obtain
        /// routes - the part of the Phase 5 exit criterion that needs Play mode.
        /// </summary>
        [UnityTest]
        public IEnumerator Agents_BuildGrid_AndAcquireRoutes()
        {
            AStarPathfindingManager pathfinding = AStarPathfindingManager.Instance;
            Assert.IsNotNull(pathfinding, "Level 1 should contain the pathfinding manager");
            Assert.IsTrue(pathfinding.IsBuilt, "the grid should build on Awake");
            Assert.Greater(pathfinding.WalkableNodeCount, 100, "the chamber should yield plenty of walkable cells");

            ObstacleAgent[] agents = Object.FindObjectsByType<ObstacleAgent>(FindObjectsInactive.Exclude);
            Assert.Greater(agents.Length, 0, "Level 1 should contain obstacles");

            // Give the agents a couple of seconds of real running to path and move.
            var startPositions = new Dictionary<ObstacleAgent, Vector3>();
            foreach (ObstacleAgent agent in agents) startPositions[agent] = agent.transform.position;

            yield return new WaitForSeconds(2.5f);

            int moved = 0;
            foreach (ObstacleAgent agent in agents)
            {
                if ((agent.transform.position - startPositions[agent]).sqrMagnitude > 0.04f) moved++;
            }

            Assert.Greater(moved, 0, "at least one agent should have started moving along a route");

            // Nothing should have tunnelled into geometry.
            int environment = LayerMask.NameToLayer(GameConstants.LayerEnvironment);
            if (environment >= 0)
            {
                foreach (ObstacleAgent agent in agents)
                {
                    bool inside = Physics.CheckSphere(agent.transform.position + Vector3.up * 0.7f, 0.3f,
                                                      1 << environment, QueryTriggerInteraction.Ignore);
                    Assert.IsFalse(inside, $"{agent.name} ended up inside level geometry");
                }
            }

            Assert.AreEqual(0, ObstacleManager.Instance.TotalStuckRecoveries(),
                            "no agent should have needed a stuck recovery in the first seconds");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static IEnumerator WaitUntilLevelReady()
        {
            float deadline = Time.realtimeSinceStartup + 30f;

            while (Time.realtimeSinceStartup < deadline)
            {
                bool ready = GameManager.Instance != null
                             && PuzzleManager.Instance != null
                             && DDAManager.Instance != null
                             && PerformanceTracker.Instance != null
                             && AStarPathfindingManager.Instance != null
                             && GameManager.Instance.State == GameState.Playing;

                if (ready) yield break;
                yield return null;
            }

            Assert.Fail("level did not reach a playable state within 30 seconds");
        }

        /// <summary>Next unsolved puzzle of a given complexity. Pass -1 for any complexity.</summary>
        private static PuzzleData FindUnsolvedPuzzle(int complexity)
        {
            QuestionBank bank = PuzzleManager.Instance.Bank;
            Assert.IsNotNull(bank, "the level's PuzzleManager should have a question bank");

            foreach (PuzzleData puzzle in bank.Puzzles)
            {
                if (puzzle == null) continue;
                if (complexity > 0 && puzzle.Complexity != complexity) continue;
                if (complexity < 0 && puzzle.Complexity > PuzzleManager.Instance.MaxComplexity) continue;
                if (PuzzleManager.Instance.IsSolved(puzzle.PuzzleId)) continue;

                return puzzle;
            }

            return null;
        }

        /// <summary>Opens a puzzle, answers it, and waits for the panel to close.</summary>
        private static IEnumerator AnswerPuzzle(PuzzleData puzzle, bool correctly)
        {
            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle), $"could not begin '{puzzle.PuzzleId}'");
            yield return null;

            if (correctly)
            {
                Submit(puzzle, true);
            }
            else
            {
                // Cached: a demotion triggered by this very puzzle would raise the
                // allowance mid-loop and keep the loop submitting.
                int allowed = PuzzleManager.Instance.MaxAttempts;

                for (int i = 0; i < allowed && PuzzleManager.Instance.IsAcceptingAnswers; i++)
                {
                    Submit(puzzle, false);
                    yield return null;
                }
            }

            float deadline = Time.realtimeSinceStartup + PuzzleCloseTimeout;
            while (PuzzleManager.Instance.IsPuzzleActive && Time.realtimeSinceStartup < deadline) yield return null;

            Assert.IsFalse(PuzzleManager.Instance.IsPuzzleActive,
                           $"'{puzzle.PuzzleId}' did not close within {PuzzleCloseTimeout}s");
        }

        /// <summary>
        /// Submits an answer through the same manager API the UI calls, so the
        /// validation path under test is the production one.
        /// </summary>
        private static void Submit(PuzzleData puzzle, bool correct)
        {
            switch (puzzle.Type)
            {
                case PuzzleType.MultipleChoice:
                {
                    int index = correct
                        ? puzzle.CorrectOptionIndex
                        : (puzzle.CorrectOptionIndex + 1) % puzzle.Options.Length;

                    PuzzleManager.Instance.SubmitOption(index);
                    break;
                }

                case PuzzleType.BloodFlowSequence:
                {
                    var order = new List<string>(puzzle.SequenceSteps);
                    if (!correct) order.Reverse();

                    PuzzleManager.Instance.SubmitSequence(order);
                    break;
                }

                default:
                    PuzzleManager.Instance.SubmitStructure(correct ? puzzle.TargetStructureId : "not_a_real_structure");
                    break;
            }
        }
    }
}
