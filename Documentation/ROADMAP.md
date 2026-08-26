# Development roadmap

Ten phases, following the PSM1 development order. Each phase ends with something
demonstrable — nothing is "half wired up and finished later".

| Phase | Scope | Status |
|---|---|---|
| 1 | Project structure, main menu, GameManager, scene management, player controller, camera, HUD | **Done** |
| 2 | Level 1 content, puzzle system, objective system, question bank | **Done** |
| 3 | PerformanceTracker: accuracy, response time, failure tracking | **Done** |
| 4 | DDAManager, DifficultySettings, HintManager | **Done** |
| 5 | A* grid, pathfinding agents, neutrophils, monocytes, obstacles | **Done** |
| 6 | DDA + A* + performance tracking integration | **Done** |
| 7 | Firebase Auth, Firestore, session logging, offline queue | Next |
| 8 | Levels 2 and 3 in full, more puzzles | |
| 9 | Performance dashboard, UI polish, audio, animation | |
| 10 | White-box testing, functional and performance testing, UAT preparation | |

---

## Phase 1 — Foundation (complete)

Delivered: `GameBootstrap`, `GameManager`, `GameSceneManager`, `SaveManager`,
`SessionData`, `PlayerController`, `PlayerInputReader`, `PlayerHealth`,
`OrbitCameraRig`, `LevelController`, `LevelExitTrigger`, `HazardVolume`,
`AnatomyMarker`, the full UI set, and the editor generation tooling that builds all
five scenes.

Verification: see [TESTING.md](TESTING.md).

## Phase 2 — Puzzles and objectives (complete)

Delivered: `PuzzleData`, `QuestionBank`, `PuzzleResult`, `PuzzleEnums` (data layer);
`PuzzleManager`, `ObjectiveManager`, `PuzzleStation`, `AnatomyStructureTag`,
`StructurePicker`, `IInteractable` (gameplay); `PlayerInteraction` (player);
`PuzzleUI`, `DraggableLabel` (UI); `QuestionBankFactory` (content seeding).

All five PSM1 puzzle formats are implemented and all five appear in Level 1:

| Format | Answered by | Level 1 example |
|---|---|---|
| Identify structure | clicking the geometry in the 3D scene | "Click the chamber that pumps into the aorta" |
| Drag and drop label | dragging a chip onto the geometry | Aorta label onto the ascending aorta |
| Blood flow sequence | ordering steps in the panel | Left atrium → mitral → LV → aortic → aorta |
| Valve identification | clicking the valve | "Click the semilunar valve of the left heart" |
| Multiple choice | panel buttons | "Which chamber has the thickest wall?" |

**Question bank:** 38 puzzles — 14 for Level 1, 12 each for Levels 2 and 3, stored as
sub-assets of three `QuestionBank` assets in `Assets/Data`. Every puzzle carries a
complexity rating of 1–3, which is the filter Phase 4's DDA will drive.

**Answering in the world.** A structure puzzle is answered by pointing at the actual
chamber geometry, not by picking from a list. `AnatomyStructureTag` is attached to
every renderer a structure owns, so a raycast resolves a block back to
`left_ventricle`, `mitral_valve` and so on. `GameState.Puzzle` frees the cursor and
freezes the player and camera while leaving time running, so obstacles will still
move once Phase 5 lands.

**Exit gating.** `LevelController.CanCompleteLevel()` holds the exit shut until every
puzzle objective is done, so Level 1 cannot be finished by walking past the anatomy.

**Exit criterion met:** answering a station's puzzle ticks its clipboard row.

## Phase 3 — Performance tracking (complete)

Delivered: `DDA/PerformanceTracker.cs`, `DDA/LevelPerformance.cs`,
`DDA/PerformanceSnapshot.cs`, `DDA/ScoreRules.cs`, plus
`Editor/PerformanceSelfCheck.cs`.

`PerformanceTracker` lives on the persistent `[Cardio Systems]` object and
**listens** — gameplay never calls it. It subscribes to `PuzzleManager`
(`PuzzleAnswered`, `AttemptSubmitted`, `HintRequested`) and `PlayerHealth`
(`Damaged`, `BloodCountChanged`), re-attaching on `SceneManager.sceneLoaded` so the
dependency arrow keeps pointing one way: gameplay → measurement, never back.

Measured per level, into one `LevelPerformance` record whose field list is already
the shape of a Firestore SESSION_LOGS document:

| Group | Fields |
|---|---|
| Puzzles | attempted, correct, failed, incorrect answers, hints, total response seconds |
| Derived | accuracy, mean response time, mean wrong answers per puzzle |
| Survival | Blood Count losses, damage taken, lowest Blood Count |
| Outcome | score, final difficulty, duration, completed, max consecutive failures |

**Scoring** (`ScoreRules`, a pure function with every weight Inspector-tunable):

```
correct: (100 x complexity) - (25 x wrong attempts) - (20 x hints)
         + speed bonus (up to 50, linear from instant down to par)
         floored at 10
failed:  0        (never negative)

par seconds = 20 at complexity 1, +12 per complexity step
```

**Recent-form window.** `PerformanceSnapshot` reports both whole-level and
last-5-puzzle figures, plus a complexity-normalised `RecentPaceRatio` (1.0 = exactly
par). Phase 4 reads this rather than the session average, so a strong opening cannot
mask a player who has started struggling.

**Exit criterion met:** verified headlessly by
`PSM2 ▸ Diagnostics ▸ Run Performance Metric Self-Check` — 24 assertions covering the
scoring rule, par times, pace ratio, aggregate formulas and divide-by-zero guards.
All pass. Metrics are visible in the Inspector on `[Cardio Systems]` during play and
in the end-of-level summary.

## Phase 4 — Dynamic Difficulty Adjustment (complete)

Delivered: `DDA/DifficultySettings.cs`, `DDA/DDAConfig.cs`, `DDA/DDARules.cs`,
`DDA/DDADecision.cs`, `DDA/DDAManager.cs`, `Gameplay/HintManager.cs`,
`Editor/Generation/DifficultyFactory.cs`, `Editor/DDASelfCheck.cs`.

Rule-based only, no machine learning (PSM1 rules 6–7). The policy lives in
`DDARules` as a **pure function**, separate from the manager that applies it, so it
can be evaluated against simulated input with no scene running.

```
score = AccuracyWeight x recentAccuracy          (70 at 100%)
      + SpeedWeight    x normalisedSpeed         (30 instant, 15 at par, 0 at 2x par)
      - FailurePenalty x consecutiveFailures     (20 each)
      clamped to 0..100

score >= 75                       -> promote     (blocked by a failure streak)
40 < score < 75                   -> hold
score <= 40                       -> demote
consecutiveFailures >= 3          -> demote, overriding both score and cooldown
```

Pace is judged **against the current tier's allowance**, so a player who is fast
*for Easy* can earn promotion out of it rather than being measured against a
standard they were explicitly given relief from.

Stability gates: no adjustment until 3 puzzles are resolved, and 2 puzzles must pass
between changes — except for the failure-streak override, which deliberately skips
the cooldown so a stuck player is not made to wait for help.

Tier parameters (shipped values, all Inspector-tunable):

| Tier | Complexity | Attempts | Obstacles | Hazards | Time | Hints | Auto-hint |
|---|---|---|---|---|---|---|---|
| Easy | ≤1 | 5 | 0.5× | 0.6× | 1.5× | High | 12s or 1 failure |
| Medium | ≤2 | 3 | 1.0× | 1.0× | 1.0× | Medium | 25s or 2 failures |
| Hard | ≤3 | 2 | 1.5× | 1.4× | 0.8× | Low | never |

**What the tier actually changes today:** puzzle complexity cap, attempts allowed,
hazard damage, and hint behaviour (delay, failure trigger, structure highlighting).
`ObstacleSpeedMultiplier` is computed and exposed but **nothing consumes it yet** —
the agents that read it arrive in Phase 5.

**Exit criterion met:** `PSM2 ▸ Diagnostics ▸ Run DDA Policy Self-Check` drives the
policy with simulated performance — 33 assertions, all passing. The tier promotes
Easy→Medium→Hard, demotes Hard→Medium→Easy, holds at both boundaries, and every
line printed carries the score, each weighted contribution, and the rule that fired.

## Phase 5 — A* pathfinding and obstacles (complete)

Delivered: `AI/AStarPathfindingManager.cs`, `AI/PathNode.cs`, `AI/NodeHeap.cs`,
`AI/PathfindingAgent.cs`, `AI/ObstacleAgent.cs`, `AI/ObstacleManager.cs`,
`Editor/AStarSelfCheck.cs`, plus the two obstacle prefabs.

**Grid.** A 2D lattice over XZ, each cell *sampled against the real 3D scene*: a
downward cast finds the floor, and a clearance sphere at body height rejects anything
a body could not fit through. Chamber walls, papillary muscles, the septum and the
fatty plaque all remove cells. Level 1 resolves to 705 walkable cells out of 2,944.

A full voxel volume was rejected deliberately: every agent here is ground-based under
gravity, so 3D cells would cost memory and search time no agent could ever use.

**Search.** Textbook A* — octile heuristic (admissible, so routes are optimal),
binary-heap open set, corner-cut prevention on diagonals, and a wall-proximity penalty
so paths do not scrape geometry. Per PSM1 section 14 the algorithm is **not** touched
by difficulty; the DDA only scales agent speed.

**Obstacles.** Neutrophils (small, fast, hunt with hysteresis on detection range) and
monocytes (large, slow, patrol fixed routes). One component with a kind/behaviour
enum rather than two near-identical subclasses. Both move through a CharacterController,
giving a *physical* guarantee against wall clipping on top of the path guarantee.

**Anti-stuck.** An agent holding a path but making no progress for 1.5s discards its
route and re-paths from wherever it is, satisfying the "never permanently stuck"
requirement. `ObstacleManager.TotalStuckRecoveries()` surfaces the count for tuning.

**Exit criterion met** for everything verifiable without Play mode:
`PSM2 ▸ Diagnostics ▸ Run A* Pathfinding Self-Check` opens the real Level 1, builds
the grid from actual geometry and runs real queries — 15 assertions, all passing.
The inflow→exit route is 46.1 units at **1.00× straight-line distance**, and 23
waypoints across four routes were re-tested with 0 sitting inside geometry. Gizmos
draw the grid and the last path. The remaining half of the criterion — watching an
agent chase the player — needs a human in Play mode (TC-22).

## Phase 6 — Integration (complete)

Delivered: three assembly definitions, `Assets/Tests/PlayMode/AdaptiveLoopIntegrationTests.cs`,
and `Editor/DiagnosticsRunner.cs`.

Phase 6 owed **evidence**, not wiring — the DDA→A* connection was already live. The
three self-checks each verified one layer in isolation; none of them proved the layers
were actually connected. That needed Play mode, which needed real tests, which needed
the assembly-definition split originally deferred to Phase 10.

**Assembly structure** (pulled forward from Phase 10):

| Assembly | Contents |
|---|---|
| `Cardio.Runtime` | everything under `Assets/Scripts` except Editor |
| `Cardio.Editor` | `Assets/Scripts/Editor`, Editor-platform only |
| `Cardio.Tests.PlayMode` | `Assets/Tests/PlayMode` |

**Integration tests** drive `PuzzleManager` through the same public API the UI calls,
in a live Level 1:

| Test | Proves |
|---|---|
| `StrongPlay_PromotesTier_AndReachesEveryConsumer` | 4 fast correct answers → tracker records them → tier Easy→Medium → **and the change reaches every consumer**: complexity cap rises, attempts tighten, hazard damage rises, a live A* agent's `CurrentSpeed` rises, and a complexity-2 puzzle that was *refused* before is now accepted |
| `RepeatedFailure_DemotesTier_AndSoftensTheGame` | 3 failed puzzles → override fires → tier Hard→Medium → hazards soften, attempts increase, agents slow |
| `Agents_BuildGrid_AndAcquireRoutes` | grid builds at runtime from real geometry, agents move along routes, none end up inside geometry, no stuck recoveries needed |

The refused-then-accepted complexity-2 assertion is the single strongest piece of
evidence in the project: it shows the difficulty decision physically changing what
content the player is offered.

**Bug found and fixed.** The first run failed with `PuzzlesFailed: expected 3, was 5`.
`Resolve()` set a close timer but left the puzzle "active" for the 3.5s explanation
window, so further submissions re-entered `Evaluate` and emitted a *second*
`PuzzleResult` — double-counting into every downstream metric. The real UI hid this by
disabling its buttons, meaning the guard lived in the view rather than the model.
Fixed by splitting `IsPuzzleActive` (on screen) from `IsAcceptingAnswers` (still
taking answers), with a re-entrancy guard in `Resolve` itself.

**Exit criterion met:** 75 automated assertions — 72 headless plus 3 PlayMode
integration tests — all passing, and the game has been observed running end to end.

## Phase 7 — Firebase

`Firebase/FirebaseManager.cs`, `AuthenticationManager.cs`, `FirestoreManager.cs`.

Collections per the PSM1 database design:

```
USERS         : UserID, Username, Email, AccountCreated
SESSION_LOGS  : LogID, UserID, CurrentLevel, AverageAccuracy, AvgResponseTime,
                FinalDifficultyTier, HintsUsed, FailedAttempts, SessionDate
```

Offline handling (PSM1 NFR4): on failure, serialise the session into
`SaveManager.Progress.PendingSessionLogs`, tell the player, keep playing, and flush the
queue when connectivity returns. `LoginUI` already has the two methods that need to
change, and nothing else in the game touches auth.

## Phase 8 — Levels 2 and 3

Replace the two placeholder rooms. Level 2: branching cerebral vessels, narrow paths,
maze structure, static blockades, moving obstacles. Level 3: right ventricle with
higher obstacle density and the hardest questions.

## Phase 9 — Dashboard and polish

`UI/DashboardUI.cs` reading completed levels, accuracy, mean response time, difficulty
reached, puzzles completed, mistakes, hints used and session history. Then audio,
animation and visual polish — last, per the PSM1 priority order.

## Phase 10 — Testing

Partly done already: the assembly-definition split landed in Phase 6, and there are
75 automated assertions (3 self-check suites + PlayMode integration tests).

What remains: EditMode tests for the pure classes now that assemblies allow it,
functional tests of the full loop by hand against the TC list, performance measurement
against the 60 FPS target on a standard laptop, and UAT preparation — the metrics in
`SessionData` and `LevelPerformance` are what gets compared against the questionnaire
and interview results.
