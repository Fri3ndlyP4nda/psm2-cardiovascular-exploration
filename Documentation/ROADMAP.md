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
| 7 | Supabase Auth, session logging, offline queue | **Done** |
| 8 | Levels 2 and 3 in full, more puzzles | **Done** |
| 9 | Performance dashboard, UI polish, audio, animation | **Done** |
| 10 | White-box testing, functional and performance testing, UAT preparation | **Automated half done; manual pass owed** |

> ## ⚠ THE PROJECT IS NOT COMPLETE
>
> All ten phases are now implemented, but **no human has ever played this build**.
> That is not a small remainder: every claim in this document was verified by
> automated checks, and none of them clicks a button, renders a frame, or hears a
> sound.
>
> | Outstanding | Blocked on |
> |---|---|
> | **The Phase 10 manual pass** | A person. TC-01–23 by hand, the 60 FPS measurement, MR-1/2/3, and the dashboard/audio judgements. See TESTING.md, "PHASE 10 MANUAL PASS" |
>
> "Phases complete" is not "project complete".


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

**Question bank:** 38 puzzles at the close of Phase 2 — 14 for Level 1, 12 each for
Levels 2 and 3 — stored as sub-assets of three `QuestionBank` assets in `Assets/Data`.
(Phase 8 raised Levels 2 and 3 to 15 each, for 44 in total.) Every puzzle carries a
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
the shape of a SESSION_LOGS row:

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
| `Cardio.Tests.EditMode` | `Assets/Tests/EditMode` |

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

### Combat and the hint economy

Built during Phase 6 after the integration tests landed, in response to a design
change: the HINT button was removed entirely and hints became something the player
*earns*.

| Script | Responsibility |
|---|---|
| `Player/PlayerAttack.cs` | The player's attack. Range/cooldown gated, hits only leukemic blasts. |
| `AI/NpcHealth.cs` | Hit points, death and revival for a hostile. |
| `AI/LeukemicBlastAgent.cs` | The malignant white blood cell. Reuses `PathfindingAgent`/`ObstacleAgent` for movement, adds health, the `PuzzleId` it was spawned for, and respawning. |
| `Gameplay/HostileSpawnDirector.cs` | Spawns one blast per wrong answer, tagged to that `PuzzleId`; owns the all-dead respawn timer. |

**Scientific accuracy drove the design.** The hostiles are *cancerous* white blood
cells, because the plot is the body under attack by leukemia. Neutrophils and
monocytes were deliberately **not** reused as targets — they are the body's legitimate
immune defenders, and letting a red blood cell kill them would teach that immune cells
are the enemy. `ObstacleManager.Rescan()` therefore excludes anything carrying a
`LeukemicBlastAgent`, so the control-condition switch cannot disable the hostiles.

**The hint economy.** Answering wrong spawns a blast tagged to that question; killing
it delivers *that question's* hint (`HintSource.Earned`). `HintsAlwaysAvailable` was
repurposed into `PuzzleManager.HostileSpawningEnabled` — the gate that decides whether
wrong answers spawn anything at all. Scores: −10 per hostile spawned, +25 once every
question in the level is answered.

**Exit criterion met:** 168 automated checks — 72 headless self-check assertions plus
96 NUnit test cases (30 EditMode, 66 PlayMode) — all passing at the close of Phase 6, and the game has been
observed running end to end.

## Phase 7 — Supabase (complete)

Delivered: `Supabase/SupabaseConfig.cs`, `ISupabaseTransport.cs`,
`SupabaseManager.cs`, `AuthenticationManager.cs`, `SessionLogManager.cs`,
`Editor/Generation/SupabaseConfigFactory.cs`, and
`Tests/PlayMode/SupabaseSyncTests.cs`.

**Supabase rather than Firebase.** The backend was changed after the phase was
deferred. It turned out to be a straight improvement for this project: Supabase is
PostgREST and GoTrue over plain HTTPS, so `UnityWebRequest` talks to it directly and
**no SDK is required at all** — no `.unitypackage` download, no native plugins, and
nothing added to `Packages/manifest.json`.

**The desktop caveat is gone.** Firebase's Windows/Mac/Linux support is classified by
Google as a prototyping workflow rather than production, which was a real problem for
a Windows PC deliverable. Supabase has no equivalent restriction: it is ordinary HTTPS
REST, and a Windows standalone build is no different from any other client.

### Schema

```
USERS         : UserID, Username, Email, AccountCreated
SESSION_LOGS  : LogID, UserID, CurrentLevel, AverageAccuracy, AvgResponseTime,
                FinalDifficultyTier, HintsUsed, FailedAttempts, SessionDate
```

Postgres columns are snake_case (`failed_attempts`, `avg_response_time`, …) because
PostgREST exposes identifiers verbatim and quoted UpperCase names are painful to work
with. The report names above map one-for-one.

> `FailedAttempts` is the **PSM1 report's** field name, kept verbatim so the schema
> matches the write-up. It means "Blood Count reached zero" and maps to C#'s
> `LevelFailures` — **not** `IncorrectAnswers`, which counts wrong answers. Confusing
> the two would quietly corrupt the evaluation data, so a test asserts the mapping
> directly. `SessionRecord` gained a `LevelFailures` field in this phase; nothing had
> been carrying it out of `LevelPerformance`.

### Auth

Anonymous sign-in. The player never sees a login form: the game signs in silently on
first launch and reuses that identity afterwards. Chosen over email/password because
UAT participants are students handed a laptop for forty minutes, and asking them to
invent credentials adds friction the study does not need.

**The refresh token is persisted, and that is the point.** An anonymous sign-in mints
a *new* user on every call. Without storing the token, each launch would strand the
previous launch's rows under a user nobody can log back into, and a participant's
second session would look like a different person.

Stated rather than glossed: that token sits in plaintext in `psm2_progress.json`.
Anyone with read access to the machine could write session logs as that anonymous
user. RLS still confines them to that one user's rows, which is a proportionate trade
for a supervised local study and would not be for anything holding personal data.

### The anon key is committed, deliberately

Supabase issues the anon key specifically to be embedded in clients. It carries the
`anon` role and grants nothing by itself — **but only because Row Level Security is
enabled.** Without RLS the same key would give full read and write access to anyone
who ran `strings` on the built executable.

That was verified against the live project rather than assumed: an unauthenticated
insert is rejected with Postgres `42501`, *"new row violates row-level security
policy"*. Hiding the key would have bought nothing while making the project harder to
build from a clean clone.

`SupabaseManager` decodes the configured key at boot and **refuses to start** if it is
not an anon key, so pasting a `service_role` key fails loudly instead of silently
shipping a database with no access control. A test guards the committed asset.

### Offline queue (PSM1 NFR4)

Unchanged in design from the Firebase plan: on failure, serialise the row into
`SaveManager.Progress.PendingSessionLogs`, keep playing, and flush when connectivity
returns. Queue-then-upload, not upload-then-queue — a crash between "upload failed"
and "write the queue" would otherwise lose the attempt.

The reconnect trigger is a successful sign-in. There is no reliable way to ask the OS
whether the internet works (`Application.internetReachability` reports the adapter,
not whether the host answers), but a successful sign-in is proof Supabase was
reachable a moment ago — the same information, without the guess.

Rows are sent oldest-first and the flush **stops** at the first transport failure,
preserving order rather than hammering a server that is not answering. A row rejected
on its own merits (4xx) is dropped with a warning instead, because retrying a
malformed row forever would block everything behind it.

**`PendingSessionLogs` is not the dashboard's data.** Phase 9's `SessionHistory` is a
separate list that is never drained, because the dashboard must keep showing a player
their history whether or not it ever reached a server. Merging them would mean a
successful sync silently wiped the dashboard. A test asserts they stay separate.

**Exit criterion met:** 250 automated checks — 120 self-check assertions plus 130
NUnit cases (41 EditMode, 89 PlayMode) — all passing.

**Verified live on 2026-09-01**, through the game's own managers and HTTP stack:
anonymous sign-in works, a row built by `SessionLogManager` reaches `session_logs` and
reads back with every column intact, `failed_attempts` carries `LevelFailures`, the
same install keeps its identity across launches, and a second anonymous user sees
**none** of the first user's rows. `SupabaseLiveRoundTripTests` is `[Explicit]` so it
does not make the deterministic suite depend on the network.

**Operational limit worth planning around:** Supabase rate-limits anonymous sign-ins
to **30 per hour per IP**. Every launch signs in, so a study day on a shared network
can hit that ceiling; past it sign-in fails quietly and rows queue locally instead of
uploading. Recorded in UAT.md.

## Phase 8 — Levels 2 and 3 (complete)

Delivered: `EnvironmentFactory.BuildCerebralVessels()`, `BuildRightVentricle()`,
`BuildJunctionDisc()`, `SceneFactory.CreateLevel2Scene()`, `CreateLevel3Scene()`,
six new world-picking puzzles, and the A* navigability checks that prove both
levels can actually be finished.

Both placeholder rooms are gone, along with `BuildPlaceholderRoom` and
`CreatePlaceholderLevelScene` - dead code whose "scheduled for Phase 8" sign would
now be a lie.

**Level 2 - cerebral circulation.** A branching vessel network rather than a room:
paired internal carotids and a vertebrobasilar trunk converging on the Circle of
Willis, built as a genuine ring because that is what it is. Corridors are 6 units
wide against Level 1's 7, for the narrow paths the phase asked for.

The static blockade is a thrombus that **seals the basilar outright**. That is the
level's teaching mechanism, not an obstacle: the only way through is the long way
round via a carotid and across the ring, which is precisely what the Circle of
Willis exists to do, and what `lv2_mc_collateral` and `lv2_mc_ischaemic_stroke`
ask about. The self-check measures the detour at 93.8 units against a 9.0 unit
straight line.

**Level 3 - right ventricle.** Built to contrast with Level 1 rather than mirror it,
because the differences are the content: a visibly thinner wall (1.4 against 2.2),
a three-cusp tricuspid inflow, a pulmonary artery rendered in deoxygenated blue,
the septum on the opposite side, and a moderator band the left ventricle has no
equivalent of. Six agents against Level 1's three - the highest density in the game.

**New puzzles.** Both banks went from 12 to 15, the ceiling PSM1 section 25 allows.
Before this phase neither level had a single world-picking puzzle, because neither
had tagged anatomy; all five formats are now reachable in Level 3 and four in
Level 2. **Level 2 has no valve puzzle on purpose** - cerebral arteries have no
valves, and a test now asserts its absence so it cannot be mistaken for an oversight.

### The bug this phase found, and the rule that came out of it

The first Level 2 build was **completely unplayable**: every station unreachable,
the spawn point sealed inside a wall. Nothing in the existing suite caught it,
because every other automated test in the project loads Level 1.

`BuildCorridor` always emits two full-length side walls, so composing a T-junction
from two overlapping corridors lays walls straight across the through-route. A
second, subtler instance survived the first fix: corridor walls extending several
units *into* a junction still split it down the middle.

The rule, now stated in ARCHITECTURE: **every junction is a disc, never two crossing
corridors**, and corridors butt at the disc edge rather than reaching inside it.

The lasting fix is the check, not the geometry. `AStarSelfCheck` now asserts that
every puzzle station and the exit are reachable from the spawn in Levels 2 and 3 -
the property that makes a level completable at all, fully decidable from the grid
with no Play mode and no human.

**Exit criterion met:** 194 automated checks - 93 self-check assertions plus 101
NUnit cases (35 EditMode, 66 PlayMode) - all passing.

## Phase 9 — Dashboard and polish (complete)

Delivered: `UI/DashboardUI.cs`, `Core/AudioManager.cs`, `Gameplay/AudioCueListener.cs`,
`Editor/Generation/AudioFactory.cs`, `SessionRecord` and the session history on
`SaveManager`, plus `SceneFactory.BuildDashboardPanel()`.

**The dashboard needed a data source that did not exist.** Phase 9 asks for session
history, but nothing in the project had ever persisted a finished attempt:
`PendingSessionLogs` is an empty list explicitly marked "Populated in Phase 7", and
`PerformanceTracker` holds only the *current* session in memory, so it is empty on
the main menu and gone after a restart.

So `SessionRecord` and `PlayerProgress.SessionHistory` were added - a local record
of finished attempts, capped at 20. It is deliberately **not** the same list as
`PendingSessionLogs`: that one is Phase 7's Supabase upload queue and gets drained
on a successful sync, so a dashboard reading it would erase itself the first time
the game went online. A test asserts the two stay separate.

This is not Phase 7 work done early. Nothing here authenticates, uploads, or talks
to the backend; it is local persistence of the kind `SaveManager` already does, and it
is the prerequisite for the dashboard rather than a stand-in for the backend.

**Dashboard.** Opened from the main menu's Profile button, which previously showed a
placeholder notice. Two columns: aggregates (levels completed, accuracy, mean
response, wrong answers, puzzles failed, hints, difficulty reached, score, time
played) and the most recent eight attempts. Accuracy is computed from summed puzzle
counts rather than by averaging each attempt's percentage, so a two-puzzle attempt
does not weigh the same as a fifteen-puzzle one. On a profile with no history it says
so, rather than showing a confident 0% that reads as "you scored nothing".

**Audio.** Six cues - correct, wrong, hint, damage, level complete, click - generated
as WAV files by `AudioFactory` rather than imported, for the same reason the scenes
and materials are generated: the project commits no binary it cannot rebuild from
source. They are **placeholder tones and the report should say so**; the claim being
made is that the right event makes a sound at the right moment, not that it sounds
good. `AudioCueListener` lives in Gameplay and subscribes to existing events, so no
gameplay class learns that audio exists and `AudioManager` stays in Core without
reaching upward. Master volume needed no work - `SettingsPanel` already drives
`AudioListener.volume`.

### The bug this phase found

`PerformanceTracker` began a level only on a **state transition** into Playing. But
`SetState` ignores a transition to the state it is already in, so a level entered
while already Playing - a restart, or replaying the same level - raised no event,
`BeginLevel` never ran, and `FinishLevel` then early-returned on a None active
level. That level recorded **nothing**: no metrics, no dashboard entry, silently.

It had been latent since Phase 3 and no existing test caught it, because every test
reached a level through a state change. The Phase 9 test asserting that *dying*
writes a history record is what exposed it. Fixed by beginning the level from
`SessionChanged`, which `NotifyLevelStarted` always raises, rather than from the
state edge.

**Exit criterion met:** 209 automated checks - 93 self-check assertions plus 116
NUnit cases (41 EditMode, 75 PlayMode) - all passing.

**Not done, and deliberately:** animation beyond what already existed. Station
bobbing, the solved-state colour change and structure highlighting were built in
earlier phases; nothing further was added, because the PSM1 priority order puts
visual polish last and the remaining budget is better spent on Phase 10 and the
manual playtest that has still never happened.

## Phase 10 — Testing (automated half complete; manual pass owed)

Delivered: `Editor/PerformanceBudgetCheck.cs`, `Editor/UatExport.cs`,
`Tests/PlayMode/FullLoopFunctionalTests.cs`, and [UAT.md](UAT.md).

**238 automated checks** — 120 self-check assertions plus 118 NUnit cases, all
passing, zero compiler warnings.

**Full-loop functional testing, automated half.** `FullLoopFunctionalTests` plays a
level start to finish: solves every station the tier will open, then asserts the
tracker, the objective board, the save file and the dashboard record all agree about
what happened. That is the strongest end-to-end claim available without a person, and
it is explicitly *not* the manual TC pass.

**Performance, automated half.** `PerformanceBudgetCheck` verifies across all three
levels that the decisions ARCHITECTURE section 8 credits for the 60 FPS target are
actually in the built scenes: one directional light, no real-time shadows anywhere,
every environment renderer opted out of casting and receiving, all environment
geometry static-batched, ≤16 shared materials, and a 160-unit far clip.

**It does not measure frame rate and cannot.** Batch mode has no renderer. The check
answers "are the things we said we did still done", not "is it fast"; a regression
such as shadows being re-enabled would be caught, but the number itself needs the HUD
counter on the target laptop. TC-23 remains MANUAL REQUIRED.

*Found while writing it:* the first version asserted a scene-wide cap on non-static
renderers, which failed on Levels 2 and 3. That was the check being wrong, not the
build — those levels have more stations and more agents, all of which correctly move.
Rescoped to the `Environment_*` hierarchy, where the answer is unambiguous: **zero**
environment renderers have lost their batching flag in any level.

**UAT preparation.** `PSM2 ▸ UAT ▸ Export Session Metrics to CSV` writes one row per
finished attempt from the local history — the objective half of the evaluation, in a
form a spreadsheet can open. [UAT.md](UAT.md) carries the participant protocol, a
questionnaire whose every item names the metric it should be read against, interview
prompts, and a threats-to-validity section that states plainly that the first
participant will also be this build's first player.

**Already satisfied before this phase:** EditMode tests for the pure classes
(`PuzzleContentTests`, `SavePersistenceTests`) landed earlier and were extended rather
than restarted.

### What Phase 10 does NOT include

The manual pass. TC-01 to TC-23 by hand, the FPS measurement, MR-1/2/3, and the
dashboard and audio judgements are **owed in full and have not been started**. They
need a person, and no automated result substitutes for them. See TESTING.md,
"PHASE 10 MANUAL PASS — NOT YET EXECUTED".
