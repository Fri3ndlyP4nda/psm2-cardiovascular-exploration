# Testing — Phase 10 (all phases implemented)

White-box test procedures for the systems that exist today. Each case names the script
under test, so the PSM2 report can map a test back to a code path.

Record results as Pass / Fail with the Unity version and machine used.

## Levels of testing

These are not interchangeable. A pass at one level says nothing about the level below it.

| Level | What it proves | Where |
|---|---|---|
| **API / manager** | a class's own logic is right | EditMode tests, self-checks |
| **PlayMode integration** | the classes are wired to each other in a live scene | PlayMode tests |
| **Simulated input** | the game responds correctly to key presses | PlayMode tests via `TestInputSource` |
| **Unity UI interaction** | real clicks, drags and focus behave | **manual only** |
| **Visual / UX** | it looks right and reads clearly | **manual only** |
| **Human gameplay** | it is playable, fair and teaches | **manual only** |

Simulated input drives `PlayerInputReader` through a scripted source, so
`PlayerController`, `PlayerInteraction` and `PauseMenuUI` run their production code
paths. It does **not** exercise uGUI: no button is clicked, no chip is dragged, no
`EventSystem` raycast is performed. Those remain manual.

## Automated coverage

268 automated checks run without a human — 120 self-check assertions plus 148 NUnit
test cases (43 EditMode, 105 PlayMode). Run them before every commit.

**268 of 268 pass**: 120/120 self-checks, 43/43 EditMode, 105/105 PlayMode, plus the
2 `[Explicit]` live tests correctly skipped.

### The suite does not talk to the live backend

`SupabaseManager.Awake` installs the real `UnityWebRequestTransport` and loads the
shipped, live config from `Resources`. Left alone, that meant **every PlayMode class
ran against the production Supabase project**: any test that finished a level uploaded
a row into the real `session_logs` table, and every run spent anonymous sign-ins from
the 30-per-hour-per-IP allowance that section 6c of UAT.md calls the biggest
operational risk of a study day.

`TestLevel.Load` now installs an offline transport and a config with sync switched
off. Suites that need a backend replace both afterwards — `SupabaseSyncTests` with its
scripted transport, `SupabaseLiveRoundTripTests` with the real one.

**One residual, stated rather than glossed:** `AuthenticationManager.Start` runs when
`GameBootstrap` creates the persistent managers, before any test's `SetUp` can swap
the config, so a suite run still performs **one** real anonymous sign-in per Unity
process. It writes no rows. Closing that last one would mean disabling the backend
from the test assembly at `RuntimeInitializeOnLoadMethod` time, which would also
disable it for a developer pressing Play in the Editor — a worse trade than one
request per run.

Two further tests are `[Explicit]`, excluded from that count and from the default run.
`SupabaseLiveRoundTripTests` hits the real Supabase project, so including it would make
a deterministic suite depend on the internet and would burn the anonymous sign-in rate
limit (30/hour/IP) during ordinary development. Run it deliberately:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\User\Downloads\PSM 2 Along" -runTests -testPlatform PlayMode -testFilter "SupabaseLiveRoundTripTests" -testResults live.xml
```

### The failure that used to be here, and what it actually was

Earlier revisions of this document recorded
`PlayerAndHazardTests.Movement_IsRelativeToTheCamera_NotWorldAxes` as a real,
unexplained failure: it failed in the full run and passed when its class ran alone.
**It now passes, and the cause turned out to be the backend isolation above.**

Every test class was performing live HTTP — sign-in, retries with backoff, and row
uploads — on the main thread. That stole frame time from whatever ran next, so a test
measuring how far the player walks in 0.6 seconds measured a character that had barely
moved. It explains every symptom that had been recorded as puzzling: why the test only
failed when another class had run first, why the player moved 0.11–0.25 units instead
of ~1.9, and why making `TestLevel.PlacePlayer` wait *longer* made things worse rather
than better — a longer setup simply overlapped more network activity.

Two hypotheses recorded here previously were wrong and are worth naming as such: the
player falling during the measurement was a symptom, not the cause; and obstacle
agents pinning the player, offered as the leading explanation, was never the problem.
Both were labelled unverified at the time, and neither survived contact with the
actual fix.

**None of them proves the game is playable by a person.** Not one clicks a button,
renders a frame, or hears a sound. That distinction is the whole point of the table
at the top of this document, and it is why the manual pass below is still owed.

The two units are not the same thing and the table below mixes them: the self-checks
count individual assertions, while the NUnit rows count *test cases* (a case may make
several assertions, and a `[ValueSource]` method expands into one case per value —
which is why `PuzzleContentTests` reports 26 cases from 11 methods).

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\Users\User\Downloads\PSM 2 Along" -executeMethod Cardio.EditorTools.DiagnosticsRunner.RunAll -logFile checks.log
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\User\Downloads\PSM 2 Along" -runTests -testPlatform PlayMode -testResults results.xml -logFile tests.log
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\User\Downloads\PSM 2 Along" -runTests -testPlatform EditMode -testResults edit.xml -logFile edit.log
```

| Suite | Count | Covers |
|---|---|---|
| `PerformanceSelfCheck` | 24 | scoring rule, par times, aggregate formulas, divide-by-zero guards |
| `DDASelfCheck` | 33 | difficulty policy in both directions; prints the decision table |
| `PerformanceBudgetCheck` | 27 | the rendering decisions the 60 FPS target relies on, across all three levels — **design only, not frame timing** |
| `AStarSelfCheck` | 36 | grid and pathfinding against real Level 1 geometry, plus **Levels 2 and 3 navigability**: every station and the exit reachable from the spawn, and Level 2's collateral route around the thrombus |
| `PuzzleContentTests` (EditMode) | 26 | every shipped puzzle validated and answered right and wrong; structure targets checked against **every** level scene; all five formats present per level |
| `SavePersistenceTests` (EditMode) | 15 | save/load, unlocking, corruption recovery, offline queue, **session-history round-trip, cap and reset** |
| `AdaptiveLoopIntegrationTests` (PlayMode) | 3 | the whole loop connected end to end |
| `PlayerAndHazardTests` (PlayMode) | 12 | movement, collision, jumping, pause, Blood Count, hazards, **corridor edge barriers** |
| `PuzzleFlowTests` (PlayMode) | 17 | stations, picking, answering, timing, hints, objectives, exit gating, panel visibility and Escape recovery |
| `PuzzlePanelContentTests` (PlayMode) | 5 | what the panel actually displays for each of the five formats |
| `PuzzleAffordanceTests` (PlayMode) | 7 | **hover highlighting and camera orbit during world-picking puzzles** |
| `HostileCombatTests` (PlayMode) | 9 | wrong answer spawns one tagged leukemic blast, kill delivers that question's hint, score penalty and clear-level bonus, respawn |
| `StateAndSceneTests` (PlayMode) | 13 | bootstrapping, scene loading, state machine, panel visibility |
| `FullLoopFunctionalTests` (PlayMode) | 2 | **a whole level played start to finish**: every openable station solved, tracker/objective/save/dashboard all agreeing at the end |
| `SupabaseLiveRoundTripTests` (PlayMode) | 2 *(explicit)* | **the real project**: live anonymous sign-in, a row uploaded through `SessionLogManager` and read back, and identity reuse across launches. Excluded from the default run |
| `SupabaseSyncTests` (PlayMode) | 12 | **offline queueing, reconnection, flush order, mid-flush disconnect**, SESSION_LOGS payload mapping, and the anon-key guard — all against a scripted transport, never the live server |
| `DashboardAndAudioTests` (PlayMode) | 9 | **finishing or failing a level writes a history record**, the dashboard reads it back and survives an empty profile, and each gameplay event fires its audio cue |

## TC coverage map

| TC | Automated? | Result | Manual required? | Reason |
|---|---|---|---|---|
| TC-01 Bootstrapping | **Yes** | Pass | No | Singleton count and persistence are objective |
| TC-02 Player movement | **Mostly** | Pass | Partly | Movement, camera-relative direction, wall collision, jump, pause-gating automated via simulated input. **Feel and responsiveness are MANUAL REQUIRED** |
| TC-03 Camera | No | — | **MANUAL REQUIRED** | Pitch clamp is assertable but the point of the test is framing, wall-clipping and comfort — all visual |
| TC-04 Blood Count / hazards | **Yes** | Pass | No | Damage, invulnerability window, death, reset, plaque contact and tier scaling all measurable |
| TC-05 Level flow | **Yes** | Pass | No | Spawn placement, objective publication, failure, restart |
| TC-06 Persistence | **Yes** | Pass | No | Round-trips through the real save file, including corruption |
| TC-07 Scene loading | **Yes** | Pass | No | Load completion, double-load guard, timeScale restore, build settings. **Fade appearance is visual** |
| TC-08 UI | **Partly** | Pass | **Partly** | Panel visibility vs game state automated. Buttons, sliders, toggles, window resize are **MANUAL REQUIRED** |
| TC-09 Login | **Partly** | Pass | **Partly** | Name persistence automated (TC-06). Button behaviour and on-screen message are manual |
| TC-10 Anatomy content | **Partly** | Pass | **Partly** | Structure ids verified present in the scene; wording verified non-empty. Proximity reveal, billboarding and **anatomical accuracy review are MANUAL REQUIRED** |
| TC-11 Stations / interaction | **Yes** | Pass | No | Detection, prompt visibility, interact key, abandon, solved state, difficulty lock |
| TC-12 Structure puzzles | **Mostly** | Pass | Partly | Raycast picking against real geometry and answer validation automated. **Dragging a chip with the mouse is MANUAL REQUIRED** |
| TC-13 Sequence / multiple choice | **Mostly** | Pass | Partly | Validation and manager flow automated. **Clicking the buttons is MANUAL REQUIRED** |
| TC-14 Objectives / exit gating | **Yes** | Pass | No | Ticking, gating, blocked message, completion, unlocking |
| TC-15 Hints | **Yes** | Pass | No | Manual vs automatic counting, tier-dependent triggering. **The structure glow is visual** |
| TC-16 Content integrity | **Yes** | Pass | No | All 44 puzzles validated (14 + 15 + 15) |
| TC-17 Metric arithmetic | **Yes** | Pass | No | Pure functions |
| TC-18 Metric capture | **Yes** | Pass | No | Timing, accuracy, streaks, session mirroring |
| TC-19 DDA policy | **Yes** | Pass | No | Both directions, overrides, gates |
| TC-20 DDA in play | **Yes** | Pass | No | Promotion, demotion, and every consumer receiving the change |
| TC-21 A* pathfinding | **Yes** | Pass | No | Grid, routes, clearance, blockades, **and that all three levels are completable** |
| TC-22 Obstacles in play | **Mostly** | Pass | Partly | Grid build, movement, no tunnelling, no stuck recoveries, tier speed. **Whether a chase feels threatening is MANUAL REQUIRED** |
| TC-23 Performance / 60 FPS | **Partly** | Design: Pass | **MANUAL REQUIRED** | `PerformanceBudgetCheck` verifies the decisions credited for the target — no real-time shadows, shadow casting/receiving off, environment static-batched, ≤16 shared materials, 160-unit far clip. **It does not and cannot measure FPS**: batch mode has no renderer. The number itself needs the HUD counter on the target laptop |
| TC-27 Supabase sync / offline queue | **Yes** | Pass | No | Queueing, ordering, reconnect flush, mid-flush disconnect and rejected-row handling automated against a fake transport; **the live round-trip is automated too** (`SupabaseLiveRoundTripTests`, `[Explicit]`) — real sign-in, real upload through `SessionLogManager`, read back from the real table, verified 2026-09-01. Only *in-game observation* of the Console and HUD remains manual |
| TC-25 Dashboard | **Mostly** | Pass | Partly | Record writing, aggregation, history ordering, cap and empty-profile handling automated. **Whether the panel is readable, and clicking Profile, are MANUAL REQUIRED** |
| TC-26 Audio cues | **Mostly** | Pass | Partly | Every cue file generates, loads, and fires on the right event. **Whether any of it sounds acceptable is MANUAL REQUIRED — batch mode has no audio device** |
| TC-24 Combat and earned hints | **Mostly** | Pass | Partly | Spawn-on-wrong-answer, puzzle tagging, kill→hint delivery, score penalty/bonus and respawn all automated. **Whether the blast reads as threatening rather than annoying is MANUAL REQUIRED** |

The PlayMode suites are the only ones that prove the layers are *connected*; the
others each verify one layer in isolation. Both are needed.

---

## TC-01 Bootstrapping — `GameBootstrap`

| # | Step | Expected |
|---|---|---|
| 1 | Open `MainMenu`, press Play | A `[Cardio Systems]` object appears in the Hierarchy under DontDestroyOnLoad |
| 2 | Open `Level1_LeftVentricle`, press Play | Same object appears; no `NullReferenceException` in the Console |
| 3 | Navigate menu → level → menu | Only one `[Cardio Systems]` exists at any time |

Covers the "Play from any scene" requirement and the singleton guard in
`GameManager.Awake`.

## TC-02 Player movement — `PlayerController`

| # | Step | Expected |
|---|---|---|
| 1 | Hold `W` | The player moves away from the camera, not along world +Z |
| 2 | Rotate the camera 180°, hold `W` | Movement direction follows the camera |
| 3 | Walk into a chamber wall | Movement stops; the player does not pass through or jitter |
| 4 | Press `Space` | The player jumps roughly 1.4 units and lands |
| 5 | Walk off the corridor edge, press `Space` within ~0.1 s | The jump still fires (coyote time) |
| 6 | Press `Space` just before landing | The jump fires on touchdown (jump buffer) |
| 7 | Press `Esc`, then hold `W` | The player does not move while paused |

## TC-03 Camera — `OrbitCameraRig`

| # | Step | Expected |
|---|---|---|
| 1 | Move the mouse | Yaw is unlimited; pitch clamps at −25° / +70° |
| 2 | Scroll the wheel | Distance clamps between min and max |
| 3 | Back the player into a wall | The camera pulls in; no wall clips through the near plane |
| 4 | Pause | The camera stops responding to the mouse and the cursor appears |

## TC-04 Blood Count — `PlayerHealth`, `HazardVolume`

| # | Step | Expected |
|---|---|---|
| 1 | Walk into the fatty plaque in the aorta | Blood Count drops by 10; the bar shrinks |
| 2 | Stay inside | One further −10 per second, not per frame |
| 3 | Re-enter within 1 s of a hit | No damage (invulnerability window) |
| 4 | Stay until 0 | "ATTEMPT FAILED" appears; `Session.LevelFailures` increments |
| 5 | Below 30% | The bar turns orange |
| 6 | Press Retry | The level reloads with Blood Count restored to 100 |

## TC-05 Level flow — `LevelController`, `LevelExitTrigger`

| # | Step | Expected |
|---|---|---|
| 1 | Enter Level 1 | The player spawns behind the mitral valve, facing the chamber |
| 2 | Check the clipboard | Seven objective rows are listed, all unticked — six puzzles plus the exit |
| 3 | Reach the exit marker past the aorta | "LEVEL COMPLETE" appears; all rows tick off |
| 4 | Press Next Level | Level 2 loads |
| 5 | Complete Level 3 | The Next Level button is hidden; Main Menu returns to the menu |

## TC-06 Persistence — `SaveManager`

| # | Step | Expected |
|---|---|---|
| 1 | Fresh install: check the main menu | Continue is disabled; Levels 2 and 3 show "(locked)" |
| 2 | Complete Level 1, quit, relaunch | Continue is enabled; Level 2 is unlocked |
| 3 | Open `psm2_progress.json` (`PSM2 ▸ Open Save File Folder`) | `HighestUnlockedLevel: 2`, `CompletedLevels: [1]` |
| 4 | Corrupt the file with random text, relaunch | The game starts with fresh progress and a Console warning — it does not crash |
| 5 | Settings ▸ Reset Local Progress | Levels lock again |

## TC-07 Scene loading — `GameSceneManager`

| # | Step | Expected |
|---|---|---|
| 1 | Click Start, then a level | The screen fades out, the level loads, the screen fades in |
| 2 | Click a level button repeatedly during a load | Only one load runs; a warning is logged |
| 3 | Pause, then Exit to Main Menu | `Time.timeScale` returns to 1 and the menu is responsive |
| 4 | Remove a scene from Build Settings and try to load it | A clear Console error naming the setup menu item; no crash |

## TC-08 UI — menus, HUD, objective board

| # | Step | Expected |
|---|---|---|
| 1 | Every main-menu button | Each performs its action; Profile shows the Phase 7/9 notice |
| 2 | Settings ▸ volume slider | Audio level and the % label change immediately |
| 3 | Settings ▸ sensitivity slider | Camera speed changes without leaving the panel |
| 4 | Settings ▸ fullscreen toggle | The window mode changes and survives a relaunch |
| 5 | `Esc` in gameplay | Pause opens; `Esc` again closes it |
| 6 | `Esc` inside pause ▸ Settings | The settings panel closes first, pause stays open |
| 7 | Resize the game window | The HUD scales and nothing overlaps (CanvasScaler, 1920×1080 reference) |
| 8 | Reach Level Complete | The HUD hides behind the result panel |

## TC-09 Login — `LoginUI`

| # | Step | Expected |
|---|---|---|
| 1 | Press Login or Register | Sign-in is now silent and anonymous (Supabase); these buttons are vestigial. Confirm neither fakes a success nor throws |
| 2 | Type a display name, press Continue as Guest | The main menu shows "Signed in as: <name>" |
| 3 | Relaunch | The name persists (stored in `psm2_progress.json`) |

## TC-10 Anatomy content — `AnatomyMarker`

| # | Step | Expected |
|---|---|---|
| 1 | Approach each of the six structures | Name and description appear within the reveal radius |
| 2 | Walk away | The label hides again |
| 3 | Orbit the camera around a label | The label keeps facing the camera and stays upright |
| 4 | Check content accuracy | Mitral valve = bicuspid, left atrium → left ventricle; aortic valve = three cusps, ventricle → aorta; papillary muscles anchor the mitral valve; septum divides the ventricles |

## TC-11 Puzzle stations and interaction — `PlayerInteraction`, `PuzzleStation`

| # | Step | Expected |
|---|---|---|
| 1 | Walk within ~3.5 m of a station | "[E] Examine…" prompt appears on the HUD |
| 2 | Walk away | The prompt disappears |
| 3 | Press `E` at a station | The puzzle panel opens; the player and camera stop responding; the cursor appears |
| 4 | Press `Esc` | The panel closes, play resumes, and the Console reports the puzzle was *abandoned, not recorded* |
| 5 | Re-open the same station | It opens again — abandoning is not a failure |
| 6 | Answer correctly | The station board turns green and stops bobbing; it can no longer be interacted with |

## TC-12 Structure puzzles — `StructurePicker`, `AnatomyStructureTag`

| # | Step | Expected |
|---|---|---|
| 1 | Open `lv1_id_left_ventricle`, click the chamber floor | Correct; explanation appears; panel closes after ~3.5 s |
| 2 | Open `lv1_id_mitral_valve`, click a chamber wall block | "That is not an anatomical structure" — no attempt consumed |
| 3 | Click the *aortic* valve for a mitral question | Counted as an incorrect attempt; attempts remaining shown |
| 4 | Get it wrong 3 times | Puzzle resolves as failed, correct answer explained, station stays available |
| 5 | Open `lv1_drag_aorta`, drag the chip onto the aorta corridor | Correct |
| 6 | Drag the chip onto the puzzle panel itself | "Drop the label on the chamber, not on the panel" — no attempt consumed |

## TC-13 Sequence and multiple choice — `PuzzleUI`

| # | Step | Expected |
|---|---|---|
| 1 | Open `lv1_flow_left_heart` | Five steps appear in a shuffled order, not the authored order |
| 2 | Click steps in order | Each greys out; the order line builds up with arrows |
| 3 | Press CLEAR | Selection resets; all steps re-enable |
| 4 | Submit a wrong order | Incorrect; attempts decrement |
| 5 | Submit Left Atrium → Mitral Valve → Left Ventricle → Aortic Valve → Aorta | Correct |
| 6 | Open `lv1_mc_thickest_wall` | Four options; only "Left ventricle" is accepted |

## TC-14 Objectives and exit gating — `ObjectiveManager`, `LevelController`

| # | Step | Expected |
|---|---|---|
| 1 | Enter Level 1 | Seven clipboard rows, all unticked |
| 2 | Solve one puzzle | Exactly that row ticks and strikes through |
| 3 | Walk to the exit with puzzles outstanding | "N objectives still outstanding" appears; the level does **not** end |
| 4 | Walk away from the exit | The message clears |
| 5 | Solve all six, then reach the exit | "LEVEL COMPLETE"; the exit row ticks too |
| 6 | Reload the level | All rows reset to unticked |

## TC-15 Hints — `HintManager`, `PuzzleManager`

**There is no HINT button.** It was removed in the combat rework: a hint is now either
given automatically by the tier, or earned by killing the leukemic blast that a wrong
answer spawned. `PuzzleManager.RequestHint()` still exists as a public API and is
driven by tests, but no UI calls it — so `HintSource.Requested` is not reachable by a
player today.

| # | Step | Expected |
|---|---|---|
| 1 | At Easy, open a puzzle and wait 12s without answering | A hint appears unprompted in the panel and on the HUD hint bar (`HintSource.Automatic`) |
| 2 | At Hard, open a puzzle and wait | **No** unprompted hint ever appears — Hard's rate is Low |
| 3 | Answer a puzzle wrong, then find and kill the blast it spawned | That question's hint is delivered (`HintSource.Earned`) |
| 4 | Check the end-of-level summary | Automatic and earned hints are counted separately from requested ones, and an auto-hint does **not** reduce the score |
| 5 | Open a puzzle with no authored hint | Nothing is offered and no hint is counted |

## TC-16 Content integrity — `PSM2 ▸ Content ▸ Validate Question Banks`

| # | Step | Expected |
|---|---|---|
| 1 | Run the validator | "no problems found" — all 44 puzzles across the three banks |
| 2 | Set a puzzle's `CorrectOptionIndex` beyond its option count, re-run | An error naming that puzzle id |
| 3 | Change a `TargetStructureId` to a typo, re-run | An error saying the structure does not appear in the level scene |
| 4 | Edit a question's wording, then run `PSM2 ▸ Setup ▸ Build or Rebuild Project` | The edit **survives** — banks are not overwritten by a rebuild |

## TC-17 Metric arithmetic — `ScoreRules`, `LevelPerformance` (automated)

Run `PSM2 ▸ Diagnostics ▸ Run Performance Metric Self-Check`. Expect
**"24 passed, 0 failed"**. It asserts, without entering Play mode:

| Group | Assertions |
|---|---|
| Par times | 20s / 32s / 44s for complexity 1 / 2 / 3 |
| Scoring | instant-perfect at c1 = 150 and c3 = 350; at par = 100; half par = 125; two wrong + one hint = 30; over-penalised floors at 10; failed = 0; slower than par = 100 (bonus lost, not penalised) |
| Pace ratio | 1.0 at par, 0.5 at twice the speed, 2.0 at half; scales with complexity |
| Aggregates | accuracy 2/3, mean response 10s, mean wrong 4/3; empty records return 0 rather than dividing by zero |

Headless equivalent:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\Users\User\Downloads\PSM 2 Along" -executeMethod Cardio.EditorTools.PerformanceSelfCheck.Run -logFile selfcheck.log
```

## TC-18 Metric capture in play — `PerformanceTracker`

Select `[Cardio Systems]` in the Hierarchy during Play to watch the live fields.

| # | Step | Expected |
|---|---|---|
| 1 | Enter Level 1 | A `LevelPerformance` record appears for `Level1_LeftVentricle` |
| 2 | Answer a puzzle correctly first try | `PuzzlesAttempted` 1, `PuzzlesCorrect` 1, accuracy 100%, score increases, HUD score updates immediately |
| 3 | Get one wrong then right | `IncorrectAnswers` +1; accuracy still counts the puzzle once |
| 4 | Fail a puzzle entirely (3 wrong) | `PuzzlesFailed` +1, `consecutiveFailures` +1, score unchanged (failed = 0) |
| 5 | Answer correctly after a failure | `consecutiveFailures` resets to 0; `MaxConsecutiveFailures` keeps the peak |
| 6 | Press HINT twice on one puzzle | `HintsUsed` +2 — exactly 2, not 4 (double-count regression check) |
| 7 | Take a hint then press `Esc` to abandon | `HintsUsed` still counted; no puzzle recorded as attempted |
| 8 | Walk into the plaque | `DamageTaken` rises, `LowestBloodCount` falls |
| 9 | Complete the level | Console prints the level summary line; `Completed` is true |
| 10 | Check the end-of-level panel | Puzzles solved, accuracy, avg response time, wrong answers, hints all match what you did |
| 11 | Return to menu, Start Game again | All records cleared (`ResetSession`) |

## TC-19 DDA policy — `DDARules` (automated)

Run `PSM2 ▸ Diagnostics ▸ Run DDA Policy Self-Check`. Expect **"33 passed, 0 failed"**.
It drives the shipped `DDAConfig` with simulated performance and prints a full
decision line for each case — that Console output is the decision table for the report.

| Case | Input | Expected |
|---|---|---|
| Promote | 100% accuracy, 2× par speed, no failures | Easy→Medium (score 95), Medium→Hard (92.5) |
| Ceiling | same, at Hard | Hold, reason states "already at the hardest tier" |
| Demote | 30% accuracy, 1.6× par | Hard→Medium (score 21), Medium→Easy (27) |
| Floor | same, at Easy | Hold, reason states "already at the easiest tier" |
| Hold | 65% accuracy, at par | Hold at Medium (score 60.5), between both thresholds |
| Failure override | 3 consecutive failures, 90% accuracy | Demote regardless of score |
| Override vs cooldown | same, 0 puzzles since last change | Still demotes — a stuck player does not wait |
| Small sample | 2 puzzles resolved | Deferred |
| Cooldown | strong play, 0 puzzles since change | Deferred |
| Master switch | `AdaptiveEnabled = false` | Deferred |
| Blocked promotion | promote-worthy score + 2 failures | Hold, reason names the blocking failures |

Headless:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\Users\User\Downloads\PSM 2 Along" -executeMethod Cardio.EditorTools.DDASelfCheck.Run -logFile ddacheck.log
```

## TC-20 DDA in play — `DDAManager`, `HintManager`

Select `[Cardio Systems]` during Play to watch `currentTier`, `lastScore` and
`puzzlesSinceLastChange`.

| # | Step | Expected |
|---|---|---|
| 1 | Start a level | HUD shows "Difficulty: Easy"; Console logs the applied tier |
| 2 | Answer the first 2 puzzles | Console logs `Deferred` — sample too small |
| 3 | Answer 3+ quickly and correctly | Score climbs above 75; tier promotes; Console prints the `Easy -> Medium` line with its reason |
| 4 | Check the puzzle panel after promotion | Harder (complexity-2) puzzles now appear; attempts drop from 5 to 3 |
| 5 | Fail 3 puzzles in a row | Immediate demotion, even mid-cooldown |
| 6 | After demoting to Easy, open a puzzle and wait 12s | A hint appears unprompted and the target structure glows |
| 7 | At Hard, open a puzzle and wait | **No** unprompted hint — Hard never offers one |
| 8 | Walk into the plaque at Easy vs Hard | ~6 damage vs ~14 (0.6× vs 1.4×) |
| 9 | Take an auto-hint, then solve the puzzle | Score is **not** penalised for it; summary shows it under auto-hints |
| 10 | Request a hint manually, then solve | Score **is** reduced by 20 |
| 11 | Return to menu, Start Game | Tier resets to the session's starting difficulty; decision log clears |

## TC-21 A* pathfinding — `AStarPathfindingManager` (automated)

Run `PSM2 ▸ Diagnostics ▸ Run A* Pathfinding Self-Check`. Expect **"36 passed, 0 failed"**.
It opens the real Level 1, builds the grid from actual geometry, and asserts:

| Group | Assertion |
|---|---|
| Grid | builds; has walkable space; walkable fraction plausible (currently 705/2944 = 23.9%) |
| Main route | inflow → aorta exit exists; length ≥ direct distance; not a wild detour (measured 46.1 units, 1.00× direct, 529 nodes expanded) |
| Clearance | 4 routes across the chamber, past the papillary muscles and diagonally; **every waypoint re-tested against Environment + Obstacle layers, 0 inside geometry** |
| Blockade | sealing the aorta corridor changes the answer (longer route, or unreachable); unblocking restores the original |
| Degenerate | same start/goal succeeds; out-of-bounds goal returns cleanly; a goal inside a wall is rescued by the nearest-walkable ring search |
| **Levels 2 and 3 navigable** | grid builds; **every puzzle station and the exit reachable from the spawn** — the property that makes a level completable at all |
| **Level 2 collateral route** | the thrombus seals the basilar outright, and the way round via a carotid and the Circle of Willis exists — measured at 93.8 units against a 9.0 unit straight line (10.4x) |

`PSM2 ▸ Diagnostics ▸ Dump A* Walkability Profile` prints an ASCII map of the grid
along the level spine — the fastest way to find where a route has closed up.

## TC-22 Obstacles in play — `PathfindingAgent`, `ObstacleAgent`

Select an agent in the Hierarchy during Play to see its path drawn in gizmos.

| # | Step | Expected |
|---|---|---|
| 1 | Enter Level 1 and stand still | Two neutrophils patrol; the monocyte plods along the aorta corridor |
| 2 | Walk within ~16 units of a neutrophil | It starts hunting; its gizmo path bends around the papillary muscles, never through them |
| 3 | Put a papillary muscle between you and it | It routes around, never clips through |
| 4 | Run away past 24 units | It loses interest and returns to its post (hysteresis — it should not flicker at the boundary) |
| 5 | Let a neutrophil touch you | −10 Blood Count, then at most once per second |
| 6 | Let the monocyte touch you | −15 Blood Count; it is noticeably slower and wider |
| 7 | Back an agent into a corner between the septum and a wall | It frees itself within ~1.5s; `StuckRecoveries` increments |
| 8 | Open a puzzle panel | All agents freeze — they cannot creep up while you answer |
| 9 | Watch agents at Easy vs Hard | Visibly slower (0.5×) vs faster (1.5×) — this is the Phase 4→5 integration |
| 10 | Check `ObstacleManager.TotalStuckRecoveries()` after 5 minutes | Low and stable; a climbing number means the grid needs tuning |
| 11 | Set `ObstacleManager.obstaclesEnabled = false` | Every agent disappears — the control condition for evaluation |

## TC-23 Performance — 60 FPS requirement

| # | Step | Expected |
|---|---|---|
| 1 | Read the HUD FPS counter in Level 1 | ≥ 60 FPS on the target laptop; the counter is green |
| 2 | `Window ▸ Analysis ▸ Profiler`, Rendering module | Low batch count — environment geometry is static-batched |
| 3 | Play for 5 minutes, watch the Memory module | No steady GC allocation growth from HUD updates |

Record the test machine's CPU, GPU and resolution alongside the result.

---

---

# PHASE 10 MANUAL PASS — NOT YET EXECUTED

> **Status: owed in full. No human has played this build.**
>
> Everything below needs eyes, ears, hands or subject knowledge. None of it can be
> discharged by an automated check, and none of it should be recorded as passed
> because a related API test passes. Phase 10's automated half is complete; this
> half has not been started.
>
> Run it in this order — a broken menu button makes every later item unreadable.

| Block | Covers | Why a machine cannot do it |
|---|---|---|
| **A** | Real uGUI interaction (items 1-6) | Nothing in the suite clicks, drags or focuses |
| **B** | Cursor and window (7-8) | Needs a window manager |
| **C** | Camera feel (9-11) | "Comfortable" is not measurable |
| **D** | Visual feedback (11a-15) | Needs a rendered frame |
| **D2** | Levels 2 and 3 (15a-15e) | Pacing and legibility; **includes MR-1 and MR-3** |
| **D3** | Dashboard and audio (15f-15j) | Needs a screen and a speaker; **includes MR-2 on audio** |
| **E** | Anatomical accuracy (16-17b) | Needs your subject knowledge; **this is MR-2** |
| **F** | Performance / 60 FPS (18-19) | Batch mode has no renderer |
| **G** | Balance and enjoyment (20-24) | Wholly subjective, and the actual research question |

The three MR items from Phase 8 are folded in rather than tracked separately:
**MR-1** is item 15a, **MR-2** is items 16-17b plus 15i for audio, **MR-3** is item
15c. They remain listed in "Manual review needed" above with the reasoning for why
each is undecidable by test.

---

# MANUAL PLAYTEST CHECKLIST

Only things that genuinely cannot be automated. Everything else above is covered by
the 250 automated checks — do not re-test it by hand.

Open `Assets/Scenes/MainMenu.unity`, press Play.

### A. Real UI interaction (no automated test touches uGUI)
1. Click every main-menu button. Each does what it says; Profile shows the Phase 7/9 notice.
2. Drag the **volume** and **sensitivity** sliders — values change live, camera speed changes without leaving the panel.
3. Toggle **fullscreen**; relaunch and confirm it stuck.
4. In a puzzle, **click** a multiple-choice option and **click** sequence steps in order.
5. In a drag-and-drop puzzle, **drag the label chip onto the structure in the chamber**. Then drop one on the panel itself and confirm it is rejected politely.
6. Press the CLOSE button with the mouse. (There is no HINT button — hints are automatic or earned by killing a blast.)

### B. Cursor and window
7. In gameplay the cursor is hidden and captured; opening a puzzle releases it; closing recaptures it.
8. Resize the game window — HUD scales, nothing overlaps or clips.

### C. Camera feel (TC-03)
9. Mouse-look: pitch stops at roughly −25°/+70°, yaw is unlimited, scroll zoom clamps.
10. Back the player into a wall — the camera pulls in and no geometry clips through it.
11. Chase a neutrophil around the papillary muscles — the camera stays usable.

### D. Visual feedback
11a. In a world-picking puzzle, sweep the mouse over the chamber — structures light **blue** under the pointer. Hold **right mouse** and drag to look around while the panel is open.
11b. Trigger a hint on the same puzzle — the answer lights **yellow**, and stays lit when the pointer moves away.
12. A hint fires at Easy — does the **correct structure visibly glow**?
13. Solve a station — does the board **turn green and stop bobbing**?
14. Approach each of the six anatomy markers — labels appear, hide on leaving, and **stay facing the camera**.
15. Blood Count below 30% — does the bar turn orange?

### D2. Levels 2 and 3 (new in Phase 8 — never played by anyone)
15a. Level 2: walk into the basilar and confirm the thrombus visibly blocks it, then find the way round via a carotid. Does the detour read as *discovering collateral circulation*, or just as a wall and a long walk?
15b. Level 2: do the narrow 6-unit vessels feel claustrophobic in a good way, or is the camera fighting the walls?
15c. Level 3: is the right ventricle wall visibly thinner than Level 1's, without being told? That contrast is the teaching point.
15d. Level 3: does the blue pulmonary artery read as "deoxygenated", and is the moderator band legible as a structure rather than scenery?
15e. Level 3 has six agents against Level 1's three. Is that dense or merely noisy?

### D3. Dashboard and audio (new in Phase 9)
15f. Main menu > **Profile**: does the panel open, and are both columns readable at your resolution? Nothing automated has ever clicked this button.
15g. Play a level to the end, return to the menu, reopen Profile — does your attempt appear in RECENT ATTEMPTS with sensible numbers?
15h. On a fresh profile (Settings > Reset Local Progress), does the dashboard say "no attempts recorded yet" rather than showing a confident 0%?
15i. **Audio:** are the six cues audible, correctly timed, and not annoying? They are procedurally generated placeholder tones, not designed sound — judge whether they are good enough to keep or should be replaced with real assets.
15j. Does the master volume slider in Settings actually scale the cues?

### D4. Supabase sync — *automated as of 2026-09-01; only in-game observation remains*
> 15k–15m and 15o are now covered by `SupabaseLiveRoundTripTests`, which signs in,
> uploads and reads back against the real project. What is left is confirming the
> *game* shows it: the Console line, and the queue behaving during real play.
15k. Enable **Authentication → Sign In / Providers → Anonymous sign-ins** in the Supabase dashboard, then launch the game. The Console should log `[Supabase] Signed in anonymously as <uuid>`.
15l. Finish a level, then check **Table Editor → session_logs** in Supabase. A row should appear with your level, accuracy and difficulty tier.
15m. Confirm `failed_attempts` holds Blood-Count-zero count, **not** wrong answers — die once on purpose and check the number.
15n. Disconnect the network, finish a level, confirm play is uninterrupted and `PendingSessionLogs` in `psm2_progress.json` grows. Reconnect, relaunch, confirm the queue drains and the row appears in Supabase.
15o. Relaunch twice and confirm both sessions report the **same** user id — a new id each launch means the refresh token is not persisting.

### E. Anatomical accuracy review (needs your subject knowledge)
16. Read all 44 explanations. Confirm mitral = bicuspid, aortic/pulmonary = semilunar with three cusps, papillary muscles anchor via chordae tendineae, septum divides the ventricles, pulmonary artery carries deoxygenated blood.
17. Confirm the Level 1 layout reads as a left ventricle to someone who knows the anatomy.
17a. **Level 2 (new):** confirm the Circle of Willis ring, the carotid/vertebrobasilar split, and the claim that a basilar occlusion can be bypassed collaterally all hold up. The level's whole structure asserts this.
17b. **Level 3 (new):** confirm tricuspid = 3 cusps, pulmonary valve = semilunar, pulmonary artery carries deoxygenated blood, the moderator band is RV-only, and that the septum reads correctly from the right side.

### F. Performance (TC-23)
18. Watch the HUD FPS counter for 5 minutes in Level 1 — **≥ 60 FPS**, counter green. Record CPU/GPU/resolution.
19. Profiler ▸ Memory: no steady GC growth.

### G. Balance and enjoyment (the whole point, and wholly subjective)
20. Is a Hard-tier neutrophil chase threatening or merely irritating?
21. When the DDA demotes you, does it feel like relief or like being patronised?
22. Are 3 attempts at Medium too few or about right?
23. At Easy, half the stations report "too advanced" — does that read as progression or as being locked out?
24. Does the puzzle panel obscure the structure it is asking about?

---

## Manual review needed — Phase 8 open questions

Three questions Phase 8 raised that **cannot be settled by any test**, logged here
rather than guessed at. They are awaiting playtest notes; nothing in Level 2 or
Level 3's gameplay or content should be tuned until those notes exist, or the
tuning is just as uninformed as the guess would have been.

| # | Question | Why no test can answer it |
|---|---|---|
| **MR-1** | **Does Level 2's thrombus read as a teaching moment?** Walk into the sealed basilar, then find the way round via a carotid and the Circle of Willis. Does that land as *discovering collateral circulation*, or merely as a wall and a detour? | The A* check proves the route exists and that the clot blocks. Whether the player *understands why* is exactly the thing a pathfinding assertion cannot see. |
| **MR-2** | **Are the six new puzzles and Level 2's structure anatomically sound?** Review the wording of `lv2_id_circle_of_willis`, `lv2_id_basilar_artery`, `lv2_drag_internal_carotid`, `lv3_id_right_ventricle`, `lv3_valve_backflow_atrium`, `lv3_drag_pulmonary_artery`; and confirm the carotid/vertebrobasilar split and the collateral-bypass claim hold up. | Automated tests verify a target id resolves to tagged geometry. They cannot verify the sentence is *true*. Needs subject knowledge. |
| **MR-3** | **Is Level 2's 10.4x detour instructive or tedious?** The collateral route measures 93.8 units against a 9.0 unit straight line. | A ratio is measurable; whether it is *enjoyable* is not. Tuning it without playtest notes would be guesswork dressed as a fix. |

MR-1 and MR-3 are covered in practice by checklist items 15a-15b, MR-2 by 17a-17b.
They are restated here because they are decisions pending, not just steps to perform.

## Known limitations at Phase 8

Stated explicitly so they are not mistaken for defects:

1. **Supabase anonymous sign-in is rate limited to 30 per hour per IP.** Every launch
   signs in, so a study day on one shared network can hit the ceiling well before the
   participant count suggests. Past it, sign-in fails *quietly* and rows queue locally
   instead of uploading — the game keeps working, but sync stops. See UAT.md.
2. **No human has played the game.** Simulated input now drives real movement,
   jumping, pausing and interaction, so the gap is narrower than it was — but
   nothing has exercised **uGUI**: no button has been clicked, no label dragged, no
   `EventSystem` raycast performed. Camera feel, visual feedback and balance are
   likewise unvalidated. See the MANUAL PLAYTEST CHECKLIST above.
3. The `PromoteMaxConsecutiveFailures` branch is unreachable with the shipped weights:
   at 20 points per consecutive failure, a score can never survive two failures and
   still clear the promote threshold. It is a safety net for retuning, and the
   self-check exercises it with a softened penalty rather than pretending the default
   path reaches it.
4. The art is procedural greybox voxel geometry, not MagicaVoxel assets.
5. Level 2 contains no valve puzzle, deliberately. Cerebral arteries have no valves,
   so the bank covers the other four formats there instead. `PuzzleContentTests`
   asserts this rather than leaving it to look like an oversight.
6. `HintSource.Requested` is unreachable in play. The hint button was removed in the
   combat rework, so the only hints a player can receive are `Automatic` (tier-driven)
   and `Earned` (killing a blast). `PuzzleManager.RequestHint()` is retained as a
   public API and is exercised by tests, not by the UI.
