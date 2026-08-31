# Architecture

## 1. Guiding principle

The PSM1 concept is a loop:

```
PLAY -> MEASURE PERFORMANCE -> ANALYZE -> ADJUST DIFFICULTY -> FEEDBACK -> PLAY
```

The architecture exists to make that loop a chain of small, separately testable
components rather than one large script. Each stage owns exactly one job and talks to
the next through an event or a plain method call:

```
PlayerController / PuzzleManager      produce raw gameplay events
        v
PerformanceTracker                    turns events into metrics        (Phase 3)
        v
DDAManager                            turns metrics into a tier        (Phase 4)
        v
DifficultySettings                    turns a tier into parameters     (Phase 4)
        v
PuzzleManager | HintManager | ObstacleManager   consume parameters
                                          v
                                  AStarPathfindingManager               (Phase 5)
```

Nothing downstream ever calls back upstream. That one-way flow is what allows Phase 4
to be added without editing Phase 1 or 2 code.

## 2. Layers

| Layer | Folder | Knows about |
|---|---|---|
| Core | `Assets/Scripts/Core` | nothing above it |
| Player | `Assets/Scripts/Player` | Core |
| Gameplay | `Assets/Scripts/Gameplay` | Core, Player, UI |
| UI | `Assets/Scripts/UI` | Core, Player |
| AI *(Phase 5)* | `Assets/Scripts/AI` | Core, DDA |
| DDA *(Phase 4)* | `Assets/Scripts/DDA` | Core |
| Firebase *(Phase 7)* | `Assets/Scripts/Firebase` | Core |
| Editor tooling | `Assets/Scripts/Editor` | everything (editor-only, never in a build) |

### Assemblies (Phase 6)

Four assembly definitions, added when Phase 6 needed PlayMode tests:

| Assembly | Contents | Platforms |
|---|---|---|
| `Cardio.Runtime` | everything under `Assets/Scripts` except Editor | all |
| `Cardio.Editor` | `Assets/Scripts/Editor` — generators, self-checks | Editor only |
| `Cardio.Tests.PlayMode` | `Assets/Tests/PlayMode` | all, gated on `UNITY_INCLUDE_TESTS` |
| `Cardio.Tests.EditMode` | `Assets/Tests/EditMode` | Editor only, gated on `UNITY_INCLUDE_TESTS` |

The split exists for a concrete reason: Unity's test framework cannot reference the
predefined `Assembly-CSharp`, so runtime code has to live in a named assembly before
any of it can be tested. `Cardio.Editor` is Editor-only, so no generation code ships
in the Windows build.

## 3. Scripts implemented in Phase 1

### Core

| Script | Responsibility |
|---|---|
| `GameBootstrap.cs` | Creates the persistent systems before the first scene loads, via `[RuntimeInitializeOnLoadMethod]`. Makes Play-from-any-scene work. |
| `GameManager.cs` | Singleton. Owns `GameState`, `SessionData`, pause, cursor mode, and navigation helpers. Raises `StateChanged` / `SessionChanged`. |
| `GameSceneManager.cs` | Async scene loading with fade in/out and a minimum load time. Named `GameSceneManager` to avoid shadowing `UnityEngine.SceneManagement.SceneManager`. |
| `SaveManager.cs` | JSON progress file: unlocked levels, completed levels, last profile. Already carries the `PendingSessionLogs` list that Phase 7 will use for offline sync. |
| `SessionData.cs` | In-memory shape of the Firestore `SESSION_LOGS` document. Metrics fields exist now; producers arrive in Phase 3. |
| `GameConstants.cs` | Scene names, tags, layers, level↔scene mapping, PlayerPrefs keys. |
| `GameEnums.cs` | `GameState`, `DifficultyTier`, `LevelId`. |

### Player

| Script | Responsibility |
|---|---|
| `PlayerInputReader.cs` | Static seam over input. Compiles against the classic Input Manager or the new Input System, whichever the project has enabled. |
| `PlayerController.cs` | Camera-relative movement on a `CharacterController`, with gravity, coyote time and a jump buffer. |
| `PlayerHealth.cs` | The Blood Count system. Raises `BloodCountChanged`, `Damaged`, `Died`. Does not decide what a death means. |
| `OrbitCameraRig.cs` | Third-person orbit camera with sphere-cast wall avoidance. Runs in `LateUpdate`. |

### Gameplay

| Script | Responsibility |
|---|---|
| `LevelController.cs` | One per level scene. Spawns/places the player, resets health, pushes objectives, reports level start and completion. |
| `LevelExitTrigger.cs` | Trigger volume that finishes the level. |
| `HazardVolume.cs` | Static damaging region. The Phase 1 stand-in for Phase 5's biological obstacles. |
| `AnatomyMarker.cs` | A labelled anatomical structure. Proximity signage now; the drag-and-drop puzzle target and hint highlight surface later. |

### UI

| Script | Responsibility |
|---|---|
| `ScreenFader.cs` | Self-creating full-screen fade used between scenes. |
| `MainMenuUI.cs` | Start / Continue / Profile / Settings / Exit, plus level select with locking. |
| `LoginUI.cs` | Email + password fields and guest sign-in. Login/Register are inert until Phase 7 and say so. |
| `GameplayHUD.cs` | Pure view. Blood Count bar, level, difficulty, score, hint indicator, FPS readout. |
| `ObjectiveBoardUI.cs` | The clipboard. Pre-created rows are shown/hidden — no allocation while playing. |
| `PauseMenuUI.cs` | Resume / Restart / Settings / Exit. Owns the `Esc` handling. |
| `LevelResultUI.cs` | Level-complete and attempt-failed panels, driven purely by `GameState`. |
| `SettingsPanel.cs` | Volume, sensitivity, fullscreen, invert-Y, reset progress. Stored in `PlayerPrefs`. |

### Data (Phase 2)

| Script | Responsibility |
|---|---|
| `PuzzleEnums.cs` | `PuzzleType`, `ObjectiveKind`, and the `UsesWorldPicking()` test that decides which input mode a puzzle needs. |
| `PuzzleData.cs` | One puzzle as a `ScriptableObject`. **Owns its own correctness rule** (`IsCorrectStructure`, `IsCorrectOption`, `IsCorrectSequence`) so no manager needs a switch over puzzle types. |
| `QuestionBank.cs` | One asset per level. `Query(maxComplexity)` is the Phase 4 DDA filter. |
| `PuzzleResult.cs` | Readonly struct: the contract between gameplay and measurement. |

### Gameplay (Phase 2 additions)

| Script | Responsibility |
|---|---|
| `PuzzleManager.cs` | Presents one puzzle, times it (unscaled), counts attempts and hints, validates via `PuzzleData`, emits `PuzzleResult`. Owns bookkeeping, not analysis. |
| `ObjectiveManager.cs` | Owns the objective list and is the only writer of `ObjectiveBoardUI` from Phase 2 on. Listens to `PuzzleManager` rather than being called by it. |
| `PuzzleStation.cs` | A clipboard in the world that opens one puzzle. References its puzzle by **id, not asset**, so reseeding the bank cannot break a scene. |
| `AnatomyStructureTag.cs` | Marks a renderer as belonging to a named structure, so a raycast can resolve geometry back to `mitral_valve` etc. |
| `StructurePicker.cs` | Screen position → structure, via `RaycastNonAlloc` walking all hits so an untagged prop in front cannot block the answer. |
| `IInteractable.cs` | Interface so `PlayerInteraction` never has to know about puzzles specifically. |

### DDA / measurement (Phase 3)

| Script | Responsibility |
|---|---|
| `PerformanceTracker.cs` | The MEASURE stage. Listens to `PuzzleManager` and `PlayerHealth`, keeps one `LevelPerformance` per level, mirrors aggregates onto `SessionData`. Lives on the persistent systems object; re-attaches on `sceneLoaded`. |
| `LevelPerformance.cs` | Per-level record. Field list is deliberately the shape of a Firestore SESSION_LOGS document. |
| `PerformanceSnapshot.cs` | Readonly view of current form (session **and** recent-window). The single input Phase 4's DDA reads. |
| `ScoreRules.cs` | Pure scoring function + Inspector-tunable `ScoreSettings`. No Unity state, so it is verifiable without a scene. |

### DDA / adaptation (Phase 4)

| Script | Responsibility |
|---|---|
| `DDARules.cs` | **The policy, as a pure function.** Score arithmetic + the IF rules. No Unity state, so a whole player career can be simulated headlessly. |
| `DDAManager.cs` | Lifecycle and application. Subscribes to `PerformanceTracker.MetricsUpdated`, runs `DDARules`, pushes the outcome into PuzzleManager / HintManager / hazards, and keeps an auditable decision log. |
| `DDAConfig.cs` | The whole policy as one Resources asset: three tier assets plus every weight and threshold. |
| `DifficultySettings.cs` | One tier's parameters — complexity cap, attempts, obstacle/hazard multipliers, time allowance, hint behaviour. |
| `DDADecision.cs` | One evaluation's full record: action, score, each weighted contribution, and the rule that fired. This *is* PSM1's "measurable reason". |
| `HintManager.cs` (Gameplay) | Offers help unprompted at the tier's rate; highlights the answer via `AnatomyStructureTag` → `AnatomyMarker.SetHighlighted()`. |

### AI / navigation (Phase 5)

| Script | Responsibility |
|---|---|
| `AStarPathfindingManager.cs` | Builds the navigation grid by sampling the 3D scene, and runs A*. Also `SetRegionBlocked` for runtime blockades. |
| `PathNode.cs` | One grid cell — walkability, world position, A* costs, heap index. |
| `NodeHeap.cs` | Binary heap open set. O(log n) pops instead of O(n), which is what makes several agents re-path inside the frame budget. |
| `PathfindingAgent.cs` | Requests, follows and re-requests paths. Moves through a CharacterController. Applies the DDA speed multiplier. Detects and recovers from being stuck. |
| `ObstacleAgent.cs` | Behaviour on top of the agent: chase (neutrophil) or patrol (monocyte), plus contact damage. |
| `ObstacleManager.cs` | Scene registry; can disable all obstacles wholesale for the fixed-difficulty control condition. **Excludes leukemic blasts** — they reuse `ObstacleAgent`, so a naive scan would count them as neutrophils and the control switch would disable the hostiles. |

### Combat and the hint economy (Phase 6)

Added after the Phase 6 integration tests, when the HINT button was removed and hints
became something the player earns.

| Script | Responsibility |
|---|---|
| `Player/PlayerAttack.cs` | Range- and cooldown-gated attack. Damages leukemic blasts only. |
| `AI/NpcHealth.cs` | Hit points, death and revival for one hostile. Does not decide what death means. |
| `AI/LeukemicBlastAgent.cs` | The malignant cell. Movement is reused wholesale from `PathfindingAgent`/`ObstacleAgent`; this adds health, the `PuzzleId` it was spawned for, and respawning. |
| `Gameplay/HostileSpawnDirector.cs` | Spawns one blast per wrong answer tagged to that `PuzzleId`; owns the all-dead respawn timer. |

**Why a new cell type rather than reusing the existing obstacles.** The plot is the
body under attack by a white blood cancer, so the hostiles are *cancerous* white blood
cells. Neutrophils and monocytes stay untouchable hazards because they are the body's
legitimate immune defenders — making them targets would teach that immune cells are
the enemy, which contradicts the accuracy requirement.

**Hint flow after the rework.** `HintSource` has three values: `Automatic` (the tier
offers it unprompted), `Earned` (killing the blast that a wrong answer spawned reveals
*that question's* hint), and `Requested` — which is now **unreachable in play**, since
no UI calls `PuzzleManager.RequestHint()`. `HintsAlwaysAvailable` was repurposed as
`PuzzleManager.HostileSpawningEnabled`, the gate deciding whether wrong answers spawn
anything at all.

**A CharacterController cannot be repositioned by writing `transform.position`.** It
keeps its own idea of where it is and overwrites the change on the next move, silently.
Anything relocating a blast goes through `LeukemicBlastAgent.MoveTo()`, which disables
the controller around the write.

**Why the grid is 2.5D rather than a voxel volume.** Every agent walks the floor under
gravity, exactly like the player, so 3D cells would cost memory and search time on
space no agent can occupy. Each XZ cell is instead sampled against the real 3D scene —
a downward cast for the floor, a clearance sphere for the body — which is what makes
walls, muscles, the septum and plaque all remove cells.

**The overhead-geometry trap.** The sampler collects *every* surface in a column and
picks the highest one still low enough to stand on. Taking the first hit from above
finds the top of any arch: the valve annulus sealed the chamber off from both
corridors until this was fixed. Cells inside walls are still rejected, because the
floor beneath a wall is found but the clearance test then hits the wall itself.

### Two UI rules learned the hard way

Both of these caused a total soft-lock — the player froze on pressing `E` with no
way to recover — and neither produced a Console error.

**1. A component must never live on the GameObject it deactivates.**
`PuzzleUI` was placed on `PuzzlePanel`, which the generator saves inactive. `Awake`
therefore never ran at load; `Show()` called `SetActive(true)`, Unity ran `Awake`
synchronously *inside that call*, and `Awake`'s last line switched the panel straight
back off. The panel never appeared, `Update` never ran, and the Escape handler that
returns the game to `Playing` could never fire. UI scripts now sit on their **canvas**,
toggling child panels — the pattern `PauseMenuUI` and `LevelResultUI` already used.

**2. One key, one owner.**
`PuzzleUI` and `PauseMenuUI` both read `PausePressed`. Real input reports a press to
every reader in the frame, so whichever ran first decided the outcome: if `PuzzleUI`
went first it closed the puzzle, leaving `PauseMenuUI` to see `Playing` and instantly
pause the game. `PauseMenuUI` is now the sole owner of Escape and routes it to
`AbandonPuzzle` while a puzzle is open.

### Editor tooling

| Script | Responsibility |
|---|---|
| `PSM2SetupTool.cs` | The `PSM2` menu. Orchestrates generation and applies player/quality settings. |
| `Generation/ProjectAssets.cs` | Folders, tags, layers, shared materials, guaranteed TMP font. |
| `Generation/UIFactory.cs` | uGUI widget builders (canvas, button, slider, toggle, input field, …). |
| `Generation/PrefabFactory.cs` | Builds the Bloo.D. Clot voxel prefab from primitives. |
| `Generation/EnvironmentFactory.cs` | Procedural voxel environments and lighting setup. One builder per level, plus `BuildJunctionDisc` for branching. |
| `Generation/SceneFactory.cs` | Builds all five scenes and registers them in Build Settings. |
| `Generation/EditorWiring.cs` | Assigns `[SerializeField] private` fields through `SerializedObject`. |

## 4. State machine

```
Boot ──► MainMenu ◄──────────────┐
          │   │                  │
          │   └──► Login ────────┤
          │                      │
          └──► Loading ──► Playing ──► LevelComplete ──► (next level | MainMenu)
                          │ │  ▲
                          │ │  └── Paused
                          │ └───── Puzzle
                          │
                          └──► GameOver ──► (retry | MainMenu)
```

`Puzzle` is deliberately **not** `Paused`. Pausing sets `Time.timeScale = 0`; puzzle
mode leaves it at 1, so obstacles keep moving while the player answers (which is what
makes the Phase 4 DDA pressure meaningful). What puzzle mode does change is the
cursor — released, so structures can be clicked — and player/camera input, which is
suppressed because both check for `Playing` specifically.

`GameManager.SetState` is the only place that writes `Time.timeScale` and the cursor
lock mode, so those two can never disagree with the visible UI.

## 5. Scene structure

| Scene | Build index | Contents |
|---|---|---|
| `MainMenu` | 0 | UI camera, EventSystem, `UI_MainMenu` canvas |
| `Login` | 1 | UI camera, EventSystem, `UI_Login` canvas |
| `Level1_LeftVentricle` | 2 | Environment, player, orbit camera, `UI_HUD`, `UI_Menus`, `LevelController`, `LevelExit` |
| `Level2_Brain` | 3 | Cerebral vessel network, same UI/controller structure |
| `Level3_RightVentricle` | 4 | Right ventricle and pulmonary outflow, same UI/controller structure |

No scene contains a manager that must survive a load — that is `GameBootstrap`'s job.

## 6. Level 1 anatomy

The greybox follows real blood flow through the left side of the heart:

```
   [ Left atrium passage ]
             |
        MITRAL VALVE            <- inflow, two leaflets, -Z
             |
   ┌───── LEFT VENTRICLE ─────┐
   │  papillary muscles       │  <- anchor the mitral valve
   │  interventricular septum │  <- -X wall, shared with the right ventricle
   └──────────┬───────────────┘
        AORTIC VALVE            <- outflow, +Z
             |
      ASCENDING AORTA           <- contains a fatty plaque hazard
             |
         LEVEL EXIT
```

Six `AnatomyMarker` structures carry an id, a display name and a one-line description:
`left_ventricle`, `mitral_valve`, `aortic_valve`, `papillary_muscle`,
`interventricular_septum`, `aorta`. Those ids are the keys the Phase 2 puzzle system
will match answers against.

## 6b. Levels 2 and 3 anatomy (Phase 8)

### Level 2 — cerebral circulation

```
        [ Vertebral inlet ]  <- spawn
               |
        BASILAR ARTERY  ---X---   <- THROMBUS: seals this route outright
               |
   ( ... only reachable the long way round ... )
               |
   [ int. carotid ] -> [ ascending ] -> [ middle cerebral ] 
               \                                  /
                >------ CIRCLE OF WILLIS --------<
                              |
                     CEREBRAL ARTERIES -> exit
```

Six tagged structures: `circle_of_willis`, `basilar_artery`, `internal_carotid`,
`middle_cerebral_artery`, `cerebral_arteries`, `thrombus`.

The thrombus is the level's teaching mechanism rather than an obstacle. Sealing the
basilar means the only route is via a carotid and across the ring — which is exactly
what the Circle of Willis is *for*. The A* self-check measures that detour at 10.4x
the straight-line distance, and asserts it both blocks and leaves a way round.

### Level 3 — right ventricle

```
   [ Right atrium passage ]
             |
      TRICUSPID VALVE            <- inflow, THREE cusps, -Z
             |
   ┌──── RIGHT VENTRICLE ────┐
   │  three papillary muscles │
   │  moderator band          │  <- RV-only; no left-ventricle equivalent
   │  septum on the +X wall   │  <- other side of the same wall as Level 1
   └──────────┬──────────────┘
      PULMONARY VALVE             <- semilunar, three cusps, +Z
             |
     PULMONARY ARTERY             <- rendered in deoxygenated blue
             |
         LEVEL EXIT
```

Seven tagged structures, including `moderator_band` and a `pulmonary_artery` whose
colour carries the single most-misremembered fact in the topic. The chamber wall is
1.4 thick against Level 1's 2.2, because the right ventricle pumps at roughly a
fifth of left-ventricular pressure — the contrast *is* the lesson, and
`lv3_mc_wall_thickness` asks about it directly.

### A third rule learned the hard way: every junction is a disc

`BuildCorridor` always emits two full-length side walls. Compose a T-junction from
two overlapping corridors and those walls run straight across the through-route.

The first Level 2 build did exactly this and was **completely unplayable** — every
station unreachable, the spawn sealed inside a wall. Nothing caught it, because every
other automated test in the project loads Level 1. A subtler second instance survived
the first fix: corridor walls reaching several units *into* a junction still split it.

So: **branches are made with `BuildJunctionDisc`** — a floor with a ring wall broken
by one gap per corridor, which has no interior walls to cut anything — and corridors
butt at the disc edge, intruding only far enough that their walls stay inside the gap
arc.

The durable half of the fix is the check, not the geometry. `AStarSelfCheck` now
asserts every station and the exit are reachable from the spawn in Levels 2 and 3.
That is the property that makes a level completable, and it is fully decidable from
the grid — no Play mode, no human.

## 7. Data flow today, and where later phases attach

```
   PlayerHealth.BloodCountChanged ──► GameplayHUD
   PlayerHealth.Died ──► GameManager.NotifyPlayerDied ──► GameState.GameOver ──► LevelResultUI
   LevelExitTrigger ──► LevelController.CompleteLevel ──► SaveManager.MarkLevelCompleted
                                                    └──► GameState.LevelComplete ──► LevelResultUI
```

The Phase 2 puzzle loop:

```
   PlayerInteraction (E) ──► PuzzleStation.Interact
                                   │
                                   ▼
                         PuzzleManager.BeginPuzzle ──► GameState.Puzzle
                                   │                        │
                                   ▼                        ▼
                              PuzzleUI.Show           cursor released,
                                   │                  player + camera frozen
              ┌────────────────────┼────────────────────┐
              ▼                    ▼                    ▼
        world click /        option button        sequence submit
        label drop
              └────────────────────┼────────────────────┘
                                   ▼
                    PuzzleData.IsCorrect* (validation)
                                   ▼
                     PuzzleManager.Evaluate  ──retry if attempts remain
                                   ▼
                          PuzzleResult emitted
                                   │
              ┌────────────────────┼────────────────────┐
              ▼                    ▼                    ▼
      ObjectiveManager      PuzzleStation        PerformanceTracker
      ticks the row         turns green          records + scores
                                                        │
                                          ┌─────────────┴─────────────┐
                                          ▼                           ▼
                                   SessionData              PerformanceSnapshot
                                   (HUD + summary)                    │
                                                                      ▼
                                                          DDARules.Evaluate
                                                          (pure policy)
                                                                      │
                                                                      ▼
                                                             DDAManager.Apply
                                                                      │
                        ┌──────────────────┬──────────────────┬───────┴───────┐
                        ▼                  ▼                  ▼               ▼
                 PuzzleManager       HintManager        HazardVolume    obstacle speed
                 complexity cap,     auto-hint timing,  damage x tier   [Phase 5 A*]
                 attempts            structure glow
```

The loop closes here: the difficulty the player experiences on puzzle *n+1* is a
function of how they performed on puzzles *n-4…n*. That is the PSM1 research
contribution, and every step of it is inspectable — `DDAManager.DecisionLog` holds
the score, the contributions and the triggering rule for every evaluation.

### Who writes what

A rule worth stating because it is easy to violate later: **gameplay classes do not
write metrics.** `PuzzleManager` emits events and never touches `SessionData`;
`PerformanceTracker` is the only writer of the metric fields. That is why hint
counting moved out of `PuzzleManager` in Phase 3 — having both increment
`SessionData.HintsUsed` would have doubled it.

| Field group | Sole writer |
|---|---|
| `Score`, `PuzzlesAttempted/Correct`, `IncorrectAnswers`, `PuzzlesFailed`, `TotalResponseTimeSeconds`, `HintsUsed`, `MaxConsecutiveFailures` | `PerformanceTracker` |
| `LevelFailures` | `GameManager.NotifyPlayerDied` |
| `CurrentLevel`, `CurrentDifficulty` | `GameManager` |

Attachment points already in place:

| Later phase | Attaches to |
|---|---|
| PuzzleManager (2) | `AnatomyMarker.StructureId`; `LevelController` for objectives |
| ObjectiveManager (2) | replaces `LevelController` as the writer of `ObjectiveBoardUI` |
| PerformanceTracker (3) | writes `SessionData.PuzzlesAttempted/Correct/TotalResponseTime` |
| DDAManager (4) | writes `SessionData.CurrentDifficulty`; HUD already displays it |
| HintManager (4) | `GameplayHUD.ShowHint()` and `AnatomyMarker.SetHighlighted()` |
| ObstacleManager / A* (5) | new agents; `HazardVolume` shows the damage contract they should use |
| FirebaseManager (7) | `SessionData` serialises directly; `SaveManager.PendingSessionLogs` is the offline queue |
| DashboardUI (9) | reads `SaveManager.Progress` and stored session logs |

## 8. Performance decisions

Chosen to hold 60 FPS on integrated laptop graphics:

- **No real-time shadows.** One directional light with `LightShadows.None`, every
  renderer set to `ShadowCastingMode.Off` and `receiveShadows = false`.
- **Flat ambient + fog** instead of baked GI or a skybox. Fog also hides the open tops
  of the chambers, so no ceiling geometry is needed.
- **Static batching.** All generated environment geometry is flagged
  `BatchingStatic | OccludeeStatic`.
- **Shared materials.** Twelve materials for the whole game; `AnatomyMarker` highlights
  via `MaterialPropertyBlock`, so runtime tinting still does not create instances.
- **Short far clip plane** (160 units) on the gameplay camera.
- **No per-frame allocation in the HUD.** Objective rows are pre-created and toggled.
- The on-screen FPS counter in the HUD is the measurement instrument for this
  requirement; it turns green at 60 FPS or above.
