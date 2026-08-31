# Testing — Phase 6

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

168 automated checks run without a human — 72 self-check assertions plus 96 NUnit
test cases (30 EditMode, 66 PlayMode). Run them before every commit.

The two units are not the same thing and the table below mixes them: the self-checks
count individual assertions, while the NUnit rows count *test cases* (a case may make
several assertions, and a `[ValueSource]` method expands into one case per value —
which is why `PuzzleContentTests` reports 21 cases from 9 methods).

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
| `AStarSelfCheck` | 15 | grid and pathfinding against real Level 1 geometry |
| `PuzzleContentTests` (EditMode) | 21 | every shipped puzzle validated, and answered right and wrong |
| `SavePersistenceTests` (EditMode) | 9 | save/load, unlocking, corruption recovery, offline queue |
| `AdaptiveLoopIntegrationTests` (PlayMode) | 3 | the whole loop connected end to end |
| `PlayerAndHazardTests` (PlayMode) | 12 | movement, collision, jumping, pause, Blood Count, hazards, **corridor edge barriers** |
| `PuzzleFlowTests` (PlayMode) | 17 | stations, picking, answering, timing, hints, objectives, exit gating, panel visibility and Escape recovery |
| `PuzzlePanelContentTests` (PlayMode) | 5 | what the panel actually displays for each of the five formats |
| `PuzzleAffordanceTests` (PlayMode) | 7 | **hover highlighting and camera orbit during world-picking puzzles** |
| `HostileCombatTests` (PlayMode) | 9 | wrong answer spawns one tagged leukemic blast, kill delivers that question's hint, score penalty and clear-level bonus, respawn |
| `StateAndSceneTests` (PlayMode) | 13 | bootstrapping, scene loading, state machine, panel visibility |

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
| TC-16 Content integrity | **Yes** | Pass | No | All 38 puzzles validated |
| TC-17 Metric arithmetic | **Yes** | Pass | No | Pure functions |
| TC-18 Metric capture | **Yes** | Pass | No | Timing, accuracy, streaks, session mirroring |
| TC-19 DDA policy | **Yes** | Pass | No | Both directions, overrides, gates |
| TC-20 DDA in play | **Yes** | Pass | No | Promotion, demotion, and every consumer receiving the change |
| TC-21 A* pathfinding | **Yes** | Pass | No | Grid, routes, clearance, blockades |
| TC-22 Obstacles in play | **Mostly** | Pass | Partly | Grid build, movement, no tunnelling, no stuck recoveries, tier speed. **Whether a chase feels threatening is MANUAL REQUIRED** |
| TC-23 Performance / 60 FPS | No | — | **MANUAL REQUIRED** | Batch mode has no renderer; frame timing there is meaningless |
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
| 1 | Press Login or Register | A message states Firebase auth is Phase 7. No fake success, no crash |
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
| 1 | Run the validator | "no problems found" |
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

Run `PSM2 ▸ Diagnostics ▸ Run A* Pathfinding Self-Check`. Expect **"15 passed, 0 failed"**.
It opens the real Level 1, builds the grid from actual geometry, and asserts:

| Group | Assertion |
|---|---|
| Grid | builds; has walkable space; walkable fraction plausible (currently 705/2944 = 23.9%) |
| Main route | inflow → aorta exit exists; length ≥ direct distance; not a wild detour (measured 46.1 units, 1.00× direct, 529 nodes expanded) |
| Clearance | 4 routes across the chamber, past the papillary muscles and diagonally; **every waypoint re-tested against Environment + Obstacle layers, 0 inside geometry** |
| Blockade | sealing the aorta corridor changes the answer (longer route, or unreachable); unblocking restores the original |
| Degenerate | same start/goal succeeds; out-of-bounds goal returns cleanly; a goal inside a wall is rescued by the nearest-walkable ring search |

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

# MANUAL PLAYTEST CHECKLIST

Only things that genuinely cannot be automated. Everything else above is covered by
the 168 automated checks — do not re-test it by hand.

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

### E. Anatomical accuracy review (needs your subject knowledge)
16. Read all 38 explanations. Confirm mitral = bicuspid, aortic/pulmonary = semilunar with three cusps, papillary muscles anchor via chordae tendineae, septum divides the ventricles, pulmonary artery carries deoxygenated blood.
17. Confirm the Level 1 layout reads as a left ventricle to someone who knows the anatomy.

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

## Known limitations at Phase 6

Stated explicitly so they are not mistaken for defects:

1. Firebase is not integrated. Login and Register are inert and say so on screen.
   Metrics are collected but stay in memory and in the local save file.
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
4. Level 2 and 3 question banks contain complexity-3 puzzles, but their placeholder
   scenes only host two stations each, so the DDA has little to work with there until
   Phase 8.
5. Levels 2 and 3 are labelled placeholder rooms. Their question banks are complete
   (12 puzzles each) but only the two panel-answered formats are reachable there,
   because those scenes have no tagged anatomy yet. Two stations each are placed so
   the flow is still testable.
6. The art is procedural greybox voxel geometry, not MagicaVoxel assets.
7. `HintSource.Requested` is unreachable in play. The hint button was removed in the
   combat rework, so the only hints a player can receive are `Automatic` (tier-driven)
   and `Earned` (killing a blast). `PuzzleManager.RequestHint()` is retained as a
   public API and is exercised by tests, not by the UI.
