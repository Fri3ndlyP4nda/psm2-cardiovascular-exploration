# Walkthrough and answer key

> **Internal reference for testing, grading demonstration, and QA.
> Not intended for player distribution.**
>
> Every question, answer, hint and coordinate below was extracted from the
> project's own data — the three `QuestionBank` assets in `Assets/Data` (the same
> assets `PuzzleContentTests` validates) and the generator source that builds the
> scenes. Nothing here is paraphrased or recalled. Where something could not be
> confirmed from code or data it is marked **UNVERIFIED**.
>
> Regenerate the underlying dump with `PSM2 ▸ Content ▸ Dump Question Banks to Text`.

---

## Two findings a tester should read first

### 1. Every level requires reaching the Hard tier to finish

The exit is gated by `LevelController.CanCompleteLevel()`, which returns
`ObjectiveManager.AllNonExitObjectivesComplete()` — **every** puzzle objective must be
solved before the exit opens. But `PuzzleManager` refuses to open a puzzle above the
current tier's complexity cap (`MaxPuzzleComplexity`: Easy 1, Medium 2, Hard 3), and
every level's objective list contains at least one complexity-3 puzzle.

| Level | Objectives by complexity | Minimum tier to complete |
|---|---|---|
| 1 | 3 × C1, 2 × C2, 1 × C3 | **Hard** |
| 2 | 1 × C1, 3 × C2, 3 × C3 | **Hard** |
| 3 | 2 × C1, 5 × C2, 1 × C3 | **Hard** |

So a player must perform well enough to be promoted **twice** (Easy → Medium → Hard)
before any level can be completed. A player who is demoted, or who never promotes,
is locked out of finishing — the remaining stations report "Too advanced for now".

This is a real consequence of the shipped tuning, not a bug in the sense of broken
code. It is flagged here rather than changed, because gameplay and content tuning is
pending playtest notes.

### 2. The correct multiple-choice answer is always the first option

All **22** multiple-choice puzzles across the three banks have `CorrectOptionIndex = 0`,
and `PuzzleUI.ConfigureOptions` writes options in authored order — it does **not**
shuffle them (only `ConfigureSequence` shuffles, and only sequence steps).

The correct answer is therefore always the top button, in every MCQ in the game. A
player could clear every multiple-choice puzzle without reading it. Sequence puzzles
are unaffected: their steps *are* shuffled per presentation.

Flagged, not fixed — same reason as above.

---

## How the systems work (sourced from code)

**Controls.** `E` interacts with a station (`PlayerInputReader.HardwareInteractPressed`
= `KeyCode.E`). Left mouse button both attacks (`PlayerAttack`, during play) and picks
structures (`PuzzleUI.HandleWorldClick`, during a puzzle). `Escape` is owned solely by
`PauseMenuUI`, which routes it to `AbandonPuzzle` while a puzzle is open.

**Puzzle formats and how each is solved.**

| Format | Solved by | Validated against |
|---|---|---|
| `IdentifyStructure` | Clicking the tagged geometry in the 3D scene | `TargetStructureId` |
| `ValveIdentification` | Same as above; a valve is the target | `TargetStructureId` |
| `DragAndDropLabel` | Dragging the label chip onto the geometry | `TargetStructureId` |
| `MultipleChoice` | Clicking an option button | `CorrectOptionIndex` (always 0 — see above) |
| `BloodFlowSequence` | Clicking steps in order, then submitting | Exact match to `SequenceSteps` |

**Hints.** There is no HINT button — it was removed in the combat rework. Three
sources exist in `HintSource`, of which only two are reachable in play:

- **`Automatic`** — the tier offers one unprompted. Easy: after **12s** or **1** failed
  attempt. Medium: after **25s** or **2**. Hard: **never** (`AutoHintDelaySeconds = 0`,
  `AutoHintAfterFailedAttempts = 0`).
- **`Earned`** — answering wrong spawns one leukemic blast tagged to that question;
  killing it banks *that question's* hint (`PuzzleManager.DeliverEarnedHint`).
- **`Requested`** — the API still exists but no UI calls it. Unreachable by a player.

Because Hard never auto-hints, and Hard is required to finish any level (finding 1),
**combat is the only hint source available for the complexity-3 puzzles that gate
every exit.**

**Combat.** A blast has 100 HP (`PrefabFactory`), the player's swing does 34 damage
with a 2.2-unit range and 0.5s cooldown (`PlayerAttack`) — **three swings to kill**.
Contact damage from a blast is 12. When every blast in a level is dead they all
respawn after **30s** (`HostileSpawnDirector.respawnDelay`). Scoring: −10 per hostile
spawned, +25 once every question in the level is answered.

---

# Level 1 — Left Ventricle

**Objective.** Solve all six puzzle objectives, then reach the exit past the aortic
valve. The exit stays shut until all six are done.

**Route.** Spawn `(0, 1.2, -20)` in the mitral inflow corridor → north through the
**mitral valve** at `z = -15` → the ventricle chamber (a disc of radius 15 centred on
the origin) → out through the **aortic valve** at `z = +15` → the ascending aorta
corridor → exit anchor `(0, 1.5, 26)`.

Hazard: a fatty plaque at `(1.9, 0, 20)` in the aorta, offset to +X leaving a ~2.3-unit
gap against the −X wall. Three agents patrol: neutrophils at `(8, 1, 5)` and
`(-8, 1, 7)`, a monocyte along the aorta corridor.

### Stations

| # | Position | Puzzle | Format | C | Opens at |
|---|---|---|---|---|---|
| 1 | `(5.5, 0, 1.5)` | `lv1_id_left_ventricle` | Identify | 1 | Easy |
| 2 | `(-3.5, 0, -11)` | `lv1_id_mitral_valve` | Identify | 1 | Easy |
| 3 | `(-10.5, 0, 4.5)` | `lv1_mc_thickest_wall` | MCQ | 1 | Easy |
| 4 | `(2.8, 0, 11.5)` | `lv1_drag_aorta` | Drag label | 2 | Medium |
| 5 | `(-6.5, 0, -5.5)` | `lv1_flow_left_heart` | Sequence | 2 | Medium |
| 6 | `(-2.8, 0, 11.5)` | `lv1_valve_semilunar` | Valve | 3 | **Hard** |

### Answers

**1. `lv1_id_left_ventricle`** (Identify, C1)
Prompt: *Click the chamber that pumps oxygenated blood into the aorta.*
**Answer: click the chamber floor — `left_ventricle`.**
Hint: *It is the large chamber you are standing in.*

**2. `lv1_id_mitral_valve`** (Identify, C1)
Prompt: *Click the valve blood passes through when it enters this chamber from the left atrium.*
**Answer: the inflow valve at `z = -15` — `mitral_valve`.**
Hint: *Look back towards the inflow end of the chamber.*

**3. `lv1_mc_thickest_wall`** (MCQ, C1)
Prompt: *Which heart chamber has the thickest muscular wall?*
**Answer: "Left ventricle"** (option 0). Others: Right ventricle / Left atrium / Right atrium.
Hint: *Think about which chamber has to push blood the furthest.*

**4. `lv1_drag_aorta`** (Drag label, C2)
Prompt: *Drag the label onto the vessel it names.* Chip reads **"Aorta"**.
**Answer: drop it on the aorta corridor past the aortic valve — `aorta`.**
Hint: *Follow the outflow passage past the aortic valve.*

**5. `lv1_flow_left_heart`** (Sequence, C2)
Prompt: *Put the path of oxygenated blood through the left heart into the correct order.*
**Answer (exact order; buttons appear shuffled each time):**
`Left Atrium → Mitral Valve → Left Ventricle → Aortic Valve → Aorta`
Hint: *Start in the chamber that receives blood from the pulmonary veins.*

**6. `lv1_valve_semilunar`** (Valve, C3)
Prompt: *Click the semilunar valve of the left heart — the one with three half-moon cusps.*
**Answer: the outflow valve at `z = +15` — `aortic_valve`.**
Hint: *Semilunar valves sit at the exit from a ventricle, not the entrance.*

### In the bank but not on any station (Level 1)

Reachable only if the DDA selects them elsewhere; no station references them.
`lv1_id_aortic_valve` (C1, → `aortic_valve`) · `lv1_id_papillary` (C2, → `papillary_muscle`) ·
`lv1_id_septum` (C2, → `interventricular_septum`) · `lv1_drag_left_ventricle` (C1, → `left_ventricle`) ·
`lv1_drag_mitral` (C2, → `mitral_valve`) · `lv1_valve_backflow` (C2, → `mitral_valve`) ·
`lv1_mc_blood_type` (C1, "Oxygenated blood") · `lv1_mc_chordae` (C3, "Chordae tendineae")

---

# Level 2 — Brain (cerebral circulation)

**Objective.** Solve all seven puzzle objectives, then reach the cerebral arteries.

**Route — this level's whole point.** Spawn `(0, 1.2, -35)` in the vertebral inlet
disc (centre `(0, -33)`, radius 7). The **basilar artery** runs north from there, and
**a thrombus at `z = -18` seals it completely.** The direct route does not exist.

The way through is collateral, via either carotid — they are mirror images:

```
spawn (0,-35)  ──►  inlet disc (0,-33)
                          │
                    ┌─────┴─────┐            basilar (0, z -26..-10)
                    ▼           ▼             ══ BLOCKED at z=-18 ══
     west carotid (x -20..-7)   east carotid (x 7..20)      at z = -33
                    │           │
        NW disc (-26,-33)   NE disc (26,-33)
                    │           │
      ascending (x=-26,      ascending (x=26,
       z -27..-6)             z -27..-6)
                    │           │
         W disc (-26,0)     E disc (26,0)
                    │           │
    middle cerebral (x -20..-10)  (x 10..20)   at z ≈ 0
                    └─────┬─────┘
                  CIRCLE OF WILLIS (ring, r=10 at origin)
                          │
              cerebral arteries (0, z 10..30)
                          ▼
                    exit (0, 1.5, 26)
```

The `AStarSelfCheck` measures this detour at **93.8 units against a 9.0-unit straight
line (10.4×)**. Chicanes narrow both ascending carotids; a plaque hazard sits at
`(-24.6, 0, -20)` on the west route only.

### Stations

| # | Position | Puzzle | Format | C | Opens at | On which route |
|---|---|---|---|---|---|---|
| 1 | `(0, 0, -26)` | `lv2_id_basilar_artery` | Identify | 2 | Medium | Basilar, before the clot |
| 2 | `(-16, 0, -33)` | `lv2_flow_carotid` | Sequence | 2 | Medium | West carotid |
| 3 | `(-26, 0, -12)` | `lv2_drag_internal_carotid` | Drag label | 2 | Medium | West ascending |
| 4 | `(26, 0, -12)` | `lv2_mc_ischaemic_stroke` | MCQ | 3 | **Hard** | East ascending |
| 5 | `(-26, 0, 0)` | `lv2_flow_vertebral` | Sequence | 3 | **Hard** | West corner disc |
| 6 | `(0, 0, -5)` | `lv2_id_circle_of_willis` | Identify | 1 | Easy | Inside the ring |
| 7 | `(5, 0, 3)` | `lv2_mc_collateral` | MCQ | 3 | **Hard** | Inside the ring |

> Stations 4 and 5 are on *opposite* routes. Completing this level requires walking
> both the east and the west carotid, not just one.

### Answers

**1. `lv2_id_basilar_artery`** (Identify, C2)
Prompt: *Click the vessel formed where the two vertebral arteries merge.*
**Answer: the wide corridor you spawned in — `basilar_artery`.**
Hint: *It is the single wide vessel you started the level in.*

**2. `lv2_flow_carotid`** (Sequence, C2)
Prompt: *Order the route blood takes from the heart to the front of the brain.*
**Answer:** `Aortic Arch → Common Carotid Artery → Internal Carotid Artery → Circle of Willis → Cerebral Arteries`
Hint: *Start at the vessel leaving the heart.*

**3. `lv2_drag_internal_carotid`** (Drag label, C2)
Prompt: *Drag the label onto the paired vessels that carry the anterior blood supply into the skull.* Chip reads **"Internal Carotid"**.
**Answer: drop it on either long side vessel — `internal_carotid`.**
Hint: *They are the two long vessels running down either side of the level.*

**4. `lv2_mc_ischaemic_stroke`** (MCQ, C3)
Prompt: *An ischaemic stroke occurs when:*
**Answer: "An artery supplying part of the brain becomes blocked"** (option 0).
Hint: *'Ischaemia' means an inadequate blood supply.*

**5. `lv2_flow_vertebral`** (Sequence, C3)
Prompt: *Order the vertebral route to the back of the brain.*
**Answer:** `Subclavian Artery → Vertebral Artery → Basilar Artery → Posterior Cerebral Artery`
Hint: *The two vertebral arteries join before they branch again.*

**6. `lv2_id_circle_of_willis`** (Identify, C1)
Prompt: *Click the ring of arteries that joins the front and back blood supplies of the brain.*
**Answer: the ring floor at the centre — `circle_of_willis`.**
Hint: *It is the open circular chamber at the centre of the level.*

**7. `lv2_mc_collateral`** (MCQ, C3)
Prompt: *Why is the Circle of Willis clinically important?*
**Answer: "It can reroute blood if one supplying artery becomes blocked"** (option 0).
Hint: *Consider the advantage of a loop over a one-way pipe.*

### In the bank but not on any station (Level 2)

`lv2_flow_venous_return` (C2: `Cerebral Veins → Dural Venous Sinuses → Internal Jugular Vein → Superior Vena Cava → Right Atrium`) ·
`lv2_mc_circle_of_willis` (C1, "Circle of Willis") · `lv2_mc_two_supplies` (C1, "Internal carotid and vertebral arteries") ·
`lv2_mc_basilar` (C2, "Basilar artery") · `lv2_mc_blood_brain_barrier` (C2, "Restricts which substances can pass from blood into brain tissue") ·
`lv2_mc_venous_drainage` (C2, "Internal jugular veins") · `lv2_mc_grey_matter` (C2, "Grey matter") ·
`lv2_mc_oxygen_share` (C3, "About 20%")

> **No valve puzzle exists in Level 2, deliberately.** Cerebral arteries have no
> valves; `PuzzleContentTests.EveryBank_OffersAllFivePuzzleFormats` asserts its
> absence so it cannot be mistaken for an oversight.

---

# Level 3 — Right Ventricle

**Objective.** Solve all eight puzzle objectives, then leave through the pulmonary
valve. This level has the most objectives and the densest opposition.

**Route.** Spawn `(0, 1.2, -20)` in the right atrium corridor → north through the
**tricuspid valve** (three cusps) at `z = -13` → the ventricle chamber (radius 13,
wall 1.4 thick against Level 1's 2.2) → out through the **pulmonary valve** at
`z = +13` → the pulmonary artery corridor, rendered in deoxygenated **blue** → exit
`(0, 1.5, 25)`.

Landmarks: the **moderator band** crosses the cavity at `(2.5, 5.4, 0)`, raised above
head height; the **interventricular septum** is on the **+X** side (the opposite side
from Level 1, since the RV sits on the other face of the same wall); three papillary
muscles rather than Level 1's two. Plaque hazard at `(-2.1, 0, 19)`.

**Six agents — the densest in the game:** four neutrophils at `(-7,1,-4)`, `(7,1,6)`,
`(-5,1,8)`, `(5,1,-7)`, plus monocytes patrolling the outflow corridor and the west
chamber.

### Stations

| # | Position | Puzzle | Format | C | Opens at |
|---|---|---|---|---|---|
| 1 | `(0, 0, -17)` | `lv3_mc_tricuspid` | MCQ | 1 | Easy |
| 2 | `(0, 0, 2)` | `lv3_id_right_ventricle` | Identify | 1 | Easy |
| 3 | `(-4, 0, -9)` | `lv3_valve_backflow_atrium` | Valve | 2 | Medium |
| 4 | `(-8, 0, 3)` | `lv3_flow_right_heart` | Sequence | 2 | Medium |
| 5 | `(8, 0, -4)` | `lv3_mc_wall_thickness` | MCQ | 2 | Medium |
| 6 | `(3, 0, 9)` | `lv3_drag_pulmonary_artery` | Drag label | 2 | Medium |
| 7 | `(0, 0, 22)` | `lv3_mc_pulmonary_artery` | MCQ | 2 | Medium |
| 8 | `(-6, 0, -6)` | `lv3_mc_pressure` | MCQ | 3 | **Hard** |

### Answers

**1. `lv3_mc_tricuspid`** (MCQ, C1)
Prompt: *Which valve sits between the right atrium and the right ventricle?*
**Answer: "Tricuspid valve"** (option 0).
Hint: *Its name tells you how many cusps it has.*

**2. `lv3_id_right_ventricle`** (Identify, C1)
Prompt: *Click the chamber that pumps deoxygenated blood towards the lungs.*
**Answer: the chamber floor — `right_ventricle`.**
Hint: *It is the chamber you are standing in.*

**3. `lv3_valve_backflow_atrium`** (Valve, C2)
Prompt: *Click the valve that stops blood returning to the right atrium while this chamber contracts.*
**Answer: the inflow valve at `z = -13` — `tricuspid_valve`.**
Hint: *Look back towards the inflow end, where you entered.*

**4. `lv3_flow_right_heart`** (Sequence, C2)
Prompt: *Order the path of deoxygenated blood through the right heart.*
**Answer (six steps):** `Superior Vena Cava → Right Atrium → Tricuspid Valve → Right Ventricle → Pulmonary Valve → Pulmonary Artery`
Hint: *Start with the vessel bringing blood down from the head and arms.*

**5. `lv3_mc_wall_thickness`** (MCQ, C2)
Prompt: *Why is the right ventricle wall thinner than the left ventricle wall?*
**Answer: "It pumps against the much lower resistance of the lungs"** (option 0).
Hint: *Think about pressure rather than volume.*

**6. `lv3_drag_pulmonary_artery`** (Drag label, C2)
Prompt: *Drag the label onto the vessel that carries blood from this chamber to the lungs.* Chip reads **"Pulmonary Artery"**.
**Answer: drop it on the blue outflow corridor — `pulmonary_artery`.**
Hint: *Follow the outflow past the pulmonary valve; it is rendered in blue.*

**7. `lv3_mc_pulmonary_artery`** (MCQ, C2)
Prompt: *The pulmonary artery is unusual among arteries because:*
**Answer: "It carries deoxygenated blood away from the heart"** (option 0).
Hint: *The definition of an artery is about direction, not oxygen.*

**8. `lv3_mc_pressure`** (MCQ, C3)
Prompt: *Compared with the systemic circulation, the pulmonary circulation is:*
**Answer: "A lower-pressure circuit"** (option 0).
Hint: *Delicate lung tissue could not survive systemic pressures.*

### In the bank but not on any station (Level 3)

`lv3_flow_pulmonary_circuit` (C3: `Pulmonary Artery → Lungs → Pulmonary Veins → Left Atrium`) ·
`lv3_mc_rv_blood` (C1, "Deoxygenated blood") · `lv3_mc_vena_cava` (C1, "Superior and inferior vena cava") ·
`lv3_mc_cusps` (C2, "Three") · `lv3_mc_pulmonary_valve` (C2, "Pulmonary valve") ·
`lv3_mc_gas_exchange` (C3, "In the capillaries surrounding the alveoli") ·
`lv3_mc_circuit_definition` (C3, "From the right heart to the lungs and back to the left heart")

---

## Sources

| Section | Sourced from |
|---|---|
| All questions, answers, hints, explanations | `Assets/Data/QuestionBank_*.asset`, dumped via `PSM2 ▸ Content ▸ Dump Question Banks to Text` |
| Station ids and positions | `StationSpec[]` in `SceneFactory.CreateLevel{1,2,3}Scene` |
| Objective lists | `ObjectiveSpec[]` in the same methods |
| Win condition | `LevelController.CanCompleteLevel()` → `ObjectiveManager.AllNonExitObjectivesComplete()` |
| Routes, coordinates, landmarks | `EnvironmentFactory.Build{LeftVentricle,CerebralVessels,RightVentricle}` |
| Spawn / exit anchors | `CreateAnchor("SpawnPoint" / "ExitAnchor", …)` |
| Level 2 detour measurement | `AStarSelfCheck.CheckCollateralRoute()` output |
| Tier caps and auto-hint timing | `DifficultyFactory` (Easy/Medium/Hard) |
| Hint sources | `PuzzleEnums.HintSource`, `PuzzleManager.DeliverEarnedHint` |
| Combat values | `PlayerAttack` (34 dmg / 2.2 range / 0.5s), `PrefabFactory` (100 HP), `HostileSpawnDirector` (30s) |
| Controls | `PlayerInputReader` hardware bindings |
| MCQ index finding | All 22 MCQs in the dump; `PuzzleUI.ConfigureOptions` (no shuffle) |

## UNVERIFIED

Nothing in the answer key is unverified — every question, answer and hint was read
from the shipped bank assets.

Two items are stated from code but **have never been observed running**, because no
human has played this build:

- **UNVERIFIED — confirm manually:** that a station physically *reachable* on foot at
  each listed coordinate. `AStarSelfCheck` proves every station is reachable by the
  pathfinding grid, which is a strong proxy but is not a person walking there.
- **UNVERIFIED — confirm manually:** that the drag-and-drop chip can actually be
  dropped onto the named geometry. No automated test performs a real drag; only the
  underlying `TargetStructureId` validation is covered.
