using System.Collections;
using Cardio.Core;
using Cardio.DDA;
using Cardio.Gameplay;
using Cardio.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// TC-02 (player movement) and TC-04 (Blood Count and hazards), driven by
    /// scripted input rather than a human at the keyboard.
    ///
    /// Movement is exercised through PlayerInputReader, so the code path is the
    /// production one: PlayerController does not know the input is synthetic.
    /// What these tests cannot judge is *feel* - whether the speed is pleasant
    /// or the camera framing is comfortable.
    /// </summary>
    public class PlayerAndHazardTests
    {
        /// <summary>Open floor in the middle of the ventricle, clear of geometry.</summary>
        private static readonly Vector3 OpenFloor = new Vector3(0f, 1.2f, -6f);

        private TestInputSource _input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();

            _input = new TestInputSource();
            PlayerInputReader.SetSource(_input);

            yield return TestLevel.PlacePlayer(OpenFloor);
        }

        [TearDown]
        public void TearDown() => PlayerInputReader.ClearSource();

        // ------------------------------------------------------------------
        // TC-02 movement
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator HoldingForward_MovesThePlayer()
        {
            Vector3 start = TestLevel.Player.transform.position;

            _input.MoveAxis = Vector2.up;
            yield return new WaitForSeconds(0.6f);
            _input.Reset();

            float travelled = Vector3.Distance(start, TestLevel.Player.transform.position);
            Assert.Greater(travelled, 0.5f, "holding forward should move the player");
        }

        [UnityTest]
        public IEnumerator Movement_IsRelativeToTheCamera_NotWorldAxes()
        {
            // Aim the camera along -X, then hold "forward". The player must
            // travel along -X, not world +Z.
            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "gameplay scene should have a main camera");

            var rig = camera.GetComponent<Cardio.Player.OrbitCameraRig>();
            if (rig != null) rig.enabled = false;   // stop the rig fighting the test's framing

            camera.transform.position = TestLevel.Player.transform.position + new Vector3(8f, 3f, 0f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            yield return null;

            Vector3 start = TestLevel.Player.transform.position;

            _input.MoveAxis = Vector2.up;
            yield return new WaitForSeconds(0.6f);
            _input.Reset();

            Vector3 delta = TestLevel.Player.transform.position - start;
            delta.y = 0f;

            Assert.Greater(delta.magnitude, 0.4f, "the player should have moved");
            Assert.Less(delta.x, -0.3f, $"expected travel along -X (camera forward), got {delta}");
            Assert.Less(Mathf.Abs(delta.z), Mathf.Abs(delta.x),
                        "movement should follow the camera, not the world Z axis");
        }

        [UnityTest]
        public IEnumerator WalkingIntoAWall_StopsThePlayer_WithoutPassingThrough()
        {
            // Push towards +X. The valve openings are at +Z and -Z, so this
            // direction meets solid myocardium rather than a doorway.
            yield return TestLevel.PlacePlayer(new Vector3(8f, 1.2f, 0f));

            Camera camera = Camera.main;
            var rig = camera.GetComponent<Cardio.Player.OrbitCameraRig>();
            if (rig != null) rig.enabled = false;

            camera.transform.position = TestLevel.Player.transform.position + new Vector3(-6f, 3f, 0f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            yield return null;

            _input.MoveAxis = Vector2.up;
            yield return new WaitForSeconds(2f);
            _input.Reset();
            yield return TestLevel.Frames(2);

            Vector3 finalPosition = TestLevel.Player.transform.position;

            // The wall ring sits at radius 15; nothing should get past it.
            float radius = new Vector2(finalPosition.x, finalPosition.z).magnitude;
            Assert.Less(radius, 15.5f, $"player escaped the chamber to radius {radius:0.0}");

            int environment = LayerMask.NameToLayer(GameConstants.LayerEnvironment);
            if (environment >= 0)
            {
                bool insideGeometry = Physics.CheckSphere(finalPosition + Vector3.up * 0.8f, 0.3f,
                                                          1 << environment, QueryTriggerInteraction.Ignore);
                Assert.IsFalse(insideGeometry, "player ended up inside level geometry");
            }
        }

        /// <summary>
        /// The corridors are open-ended tubes; without invisible barriers the
        /// player walks straight off the floor into the void. Both ends are
        /// pushed against hard.
        /// </summary>
        [UnityTest]
        public IEnumerator CorridorEnds_AreSealed_SoThePlayerCannotWalkOffTheEdge()
        {
            // Inflow corridor: floor ends at z = -26.
            yield return WalkHardTowards(new Vector3(0f, 1.2f, -23f), Vector3.back);

            Vector3 inflowEnd = TestLevel.Player.transform.position;
            Assert.Greater(inflowEnd.z, -27f, $"player left the inflow corridor at z={inflowEnd.z:0.0}");
            Assert.Greater(inflowEnd.y, -2f, $"player fell out of the level (y={inflowEnd.y:0.0})");

            // Aorta corridor: floor ends at z = +28.
            yield return WalkHardTowards(new Vector3(0f, 1.2f, 25f), Vector3.forward);

            Vector3 aortaEnd = TestLevel.Player.transform.position;
            Assert.Less(aortaEnd.z, 29f, $"player left the aorta corridor at z={aortaEnd.z:0.0}");
            Assert.Greater(aortaEnd.y, -2f, $"player fell out of the level (y={aortaEnd.y:0.0})");
        }

        /// <summary>Places the player and drives them hard in a world direction.</summary>
        private IEnumerator WalkHardTowards(Vector3 start, Vector3 worldDirection)
        {
            yield return TestLevel.PlacePlayer(start);

            Camera camera = Camera.main;
            var rig = camera.GetComponent<Cardio.Player.OrbitCameraRig>();
            if (rig != null) rig.enabled = false;

            camera.transform.position = TestLevel.Player.transform.position - worldDirection * 6f + Vector3.up * 3f;
            camera.transform.rotation = Quaternion.LookRotation(worldDirection, Vector3.up);
            yield return null;

            _input.MoveAxis = Vector2.up;
            yield return new WaitForSeconds(2.5f);
            _input.Reset();
            yield return TestLevel.Frames(3);
        }

        [UnityTest]
        public IEnumerator Jumping_LiftsThePlayerThenLandsAgain()
        {
            // The player is placed slightly above the floor, so wait for the
            // fall to finish rather than assuming a fixed number of frames.
            yield return TestLevel.WaitUntil(() => TestLevel.Player.IsGrounded, 5f, "player to settle on the floor");

            float groundY = TestLevel.Player.transform.position.y;

            _input.PressJump();

            // Track the apex over the next second.
            float peak = groundY;
            float deadline = Time.time + 1.2f;
            while (Time.time < deadline)
            {
                peak = Mathf.Max(peak, TestLevel.Player.transform.position.y);
                yield return null;
            }

            Assert.Greater(peak - groundY, 0.5f, "the jump should visibly lift the player");

            yield return TestLevel.WaitUntil(() => TestLevel.Player.IsGrounded, 5f, "player to land again");
            Assert.AreEqual(groundY, TestLevel.Player.transform.position.y, 0.35f, "player should land back on the floor");
        }

        [UnityTest]
        public IEnumerator PausedGame_IgnoresMovementInput()
        {
            GameManager.Instance.PauseGame();
            Assert.AreEqual(GameState.Paused, GameManager.Instance.State);

            Vector3 start = TestLevel.Player.transform.position;

            _input.MoveAxis = Vector2.up;
            yield return new WaitForSecondsRealtime(0.6f);
            _input.Reset();

            Assert.AreEqual(start, TestLevel.Player.transform.position, "the player must not move while paused");

            GameManager.Instance.ResumeGame();
            yield return null;
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State);
        }

        [UnityTest]
        public IEnumerator PauseKey_TogglesPauseAndTimeScale()
        {
            Assert.AreEqual(1f, Time.timeScale, 0.001f);

            _input.PressPause();
            yield return TestLevel.Frames(2);

            Assert.AreEqual(GameState.Paused, GameManager.Instance.State, "Esc should pause");
            Assert.AreEqual(0f, Time.timeScale, 0.001f, "pausing should freeze time");

            _input.PressPause();
            yield return TestLevel.Frames(2);

            Assert.AreEqual(GameState.Playing, GameManager.Instance.State, "Esc again should resume");
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "resuming should restore time");
        }

        // ------------------------------------------------------------------
        // TC-04 Blood Count
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Damage_ReducesBloodCount_AndRespectsInvulnerability()
        {
            PlayerHealth health = TestLevel.Health;
            Assert.IsNotNull(health);

            int max = health.MaxBloodCount;
            Assert.AreEqual(max, health.CurrentBloodCount, "level start should restore full Blood Count");

            health.TakeDamage(10);
            Assert.AreEqual(max - 10, health.CurrentBloodCount);
            Assert.IsTrue(health.IsInvulnerable, "a hit should open an invulnerability window");

            // A second hit inside the window must be ignored.
            health.TakeDamage(10);
            Assert.AreEqual(max - 10, health.CurrentBloodCount, "damage during invulnerability should be ignored");

            yield return TestLevel.WaitUntil(() => !health.IsInvulnerable, 5f, "invulnerability to expire");

            health.TakeDamage(10);
            Assert.AreEqual(max - 20, health.CurrentBloodCount, "damage after the window should apply");
        }

        [UnityTest]
        public IEnumerator BloodCountReachingZero_RaisesGameOver_AndCountsTheFailure()
        {
            PlayerHealth health = TestLevel.Health;
            int failuresBefore = GameManager.Instance.Session.LevelFailures;

            bool died = false;
            health.Died += () => died = true;

            // Drain in one blow so the invulnerability window is irrelevant.
            health.TakeDamage(health.MaxBloodCount);
            yield return TestLevel.Frames(2);

            Assert.IsTrue(died, "Died should fire when Blood Count hits zero");
            Assert.IsFalse(health.IsAlive);
            Assert.AreEqual(GameState.GameOver, GameManager.Instance.State, "the game should enter GameOver");
            Assert.AreEqual(failuresBefore + 1, GameManager.Instance.Session.LevelFailures,
                            "the failure should be recorded on the session");

            // And the measurement layer should have seen it too.
            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            Assert.GreaterOrEqual(record.LevelFailures, 1, "PerformanceTracker should record the level failure");
        }

        [UnityTest]
        public IEnumerator ResetHealth_RestoresFullBloodCount()
        {
            PlayerHealth health = TestLevel.Health;

            health.TakeDamage(30);
            Assert.Less(health.CurrentBloodCount, health.MaxBloodCount);

            health.ResetHealth();
            yield return null;

            Assert.AreEqual(health.MaxBloodCount, health.CurrentBloodCount);
            Assert.IsFalse(health.IsInvulnerable, "resetting should clear the invulnerability window");
        }

        [UnityTest]
        public IEnumerator WalkingIntoThePlaque_CostsBloodCount()
        {
            PlayerHealth health = TestLevel.Health;
            int before = health.CurrentBloodCount;

            yield return WalkIntoPlaque();

            Assert.Less(health.CurrentBloodCount, before,
                        "walking into the fatty plaque should reduce Blood Count");
        }

        [UnityTest]
        public IEnumerator HazardDamage_ScalesWithDifficultyTier()
        {
            // TC-20 step 8, objectively: the same hazard must hurt more at Hard.
            PlayerHealth health = TestLevel.Health;

            DDAManager.Instance.ForceTier(DifficultyTier.Easy, "test");
            yield return null;
            float easyMultiplier = DDAManager.Instance.HazardDamageMultiplier;

            health.ResetHealth();
            yield return WalkIntoPlaque();
            int easyDamage = health.MaxBloodCount - health.CurrentBloodCount;

            // Clear the area, wait out invulnerability, switch tier, repeat.
            yield return TestLevel.PlacePlayer(OpenFloor);
            DDAManager.Instance.ForceTier(DifficultyTier.Hard, "test");
            health.ResetHealth();
            yield return TestLevel.WaitUntil(() => !health.IsInvulnerable, 5f, "invulnerability to expire");

            float hardMultiplier = DDAManager.Instance.HazardDamageMultiplier;

            yield return WalkIntoPlaque();
            int hardDamage = health.MaxBloodCount - health.CurrentBloodCount;

            Assert.Greater(hardMultiplier, easyMultiplier, "Hard should carry a larger hazard multiplier");
            Assert.Greater(easyDamage, 0, "the plaque should have hurt at Easy too");
            Assert.Greater(hardDamage, easyDamage,
                           $"the same plaque should hurt more at Hard (easy {easyDamage}, hard {hardDamage})");
        }

        /// <summary>
        /// Walks the player into the aorta plaque and returns once damage lands.
        ///
        /// Walking rather than teleporting matters: a CharacterController only
        /// raises trigger callbacks while it is moving, so a player dropped
        /// straight into a hazard may never register as having entered it.
        /// </summary>
        private IEnumerator WalkIntoPlaque()
        {
            PlayerHealth health = TestLevel.Health;
            int before = health.CurrentBloodCount;

            // Start in the aorta corridor, short of the plaque at z 18-22.
            yield return TestLevel.PlacePlayer(new Vector3(1.9f, 1.2f, 15.5f));

            Camera camera = Camera.main;
            var rig = camera.GetComponent<Cardio.Player.OrbitCameraRig>();
            if (rig != null) rig.enabled = false;

            camera.transform.position = TestLevel.Player.transform.position + new Vector3(0f, 3f, -6f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            yield return null;

            _input.MoveAxis = Vector2.up;
            yield return TestLevel.WaitUntil(() => health.CurrentBloodCount < before, 8f,
                                             "the plaque to deal damage");
            _input.Reset();
        }
    }
}
