# Cardiovascular Exploration using Adaptive Gameplay Mechanics

PSM2 Final Year Project — a Windows PC 3D educational serious game about cardiovascular
anatomy, with rule-based Dynamic Difficulty Adjustment and A* pathfinding.

**This is an educational tool. It is not medical diagnostic software.**

| | |
|---|---|
| Engine | Unity 3D (Built-in Render Pipeline) |
| Language | C# |
| Backend | Firebase Firestore *(Phase 7 — not yet implemented)* |
| Target | Windows PC, 60 FPS on a standard laptop |
| Player character | "Bloo.D. Clot", a stylised red blood cell |

---

## Current status — Phase 6 (integration) complete

**The core research loop is closed:** play → measure → analyse → adjust → feedback → play.

Implemented and playable:

- Persistent `GameManager` with a state machine, session data and pause handling
- Async scene loading with fades, and a five-scene structure
- Third-person `PlayerController` (CharacterController based) + orbit camera
- Blood Count health system with a real damage source and a failure screen
- Main menu, level select, login screen, pause menu, settings, HUD, objective clipboard
- A generated Level 1 greybox of the **left ventricle** with anatomically placed
  mitral valve, aortic valve, papillary muscles, interventricular septum and aorta
- **All five PSM1 puzzle formats**, including drag-and-drop labels and click-to-identify
  answered against the real 3D geometry, not a list of buttons
- **38 puzzles** across three `QuestionBank` assets (14 / 12 / 12), each rated 1–3 for
  complexity so the Phase 4 DDA has something to filter
- Six puzzle stations in Level 1, one per format, standing beside the structure they
  ask about; the exit stays shut until every objective is done
- **Automatic performance tracking** — accuracy, response time, wrong answers,
  consecutive failures, hints, damage taken and a transparent score, recorded per
  level with no manual data entry, and surfaced live on the HUD and end-of-level panel
- **Rule-based dynamic difficulty** — a 0–100 performance score drives promotion and
  demotion across Easy / Medium / Hard, changing puzzle complexity, attempts allowed,
  hazard damage and how readily the game offers help. Every decision is logged with
  the score, each weighted contribution and the rule that fired
- **Adaptive hints** — at lower tiers a hint appears unprompted after a delay or a
  wrong answer, and the correct structure glows; at Hard nothing is offered
- **A\* pathfinding** over a grid sampled from the real 3D chamber, with neutrophils
  that hunt and monocytes that patrol — routing around the papillary muscles, septum
  and fatty plaque, and moving at 0.5× / 1.0× / 1.5× speed by difficulty tier
- Local JSON save file (progress + level unlocking)

**Not implemented yet, and deliberately not faked:** Firebase auth/Firestore, the
performance dashboard, and the full designs for Levels 2 and 3 (those two scenes
remain clearly-labelled placeholder rooms). **No human has played it yet** — see the
limitations list in [TESTING.md](Documentation/TESTING.md).

### Automated verification

159 assertions, all passing — 72 headless self-checks, 30 EditMode tests and 57
PlayMode tests. See the TC coverage map in [TESTING.md](Documentation/TESTING.md) for
what is automated and what still needs a human.

Headless self-checks:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\Users\User\Downloads\PSM 2 Along" -executeMethod Cardio.EditorTools.DiagnosticsRunner.RunAll -logFile checks.log
```

PlayMode integration tests, which run the whole loop in a live level:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\User\Downloads\PSM 2 Along" -runTests -testPlatform PlayMode -testResults results.xml -logFile tests.log
```

EditMode tests:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\User\Downloads\PSM 2 Along" -runTests -testPlatform EditMode -testResults edit.xml -logFile edit.log
```

See [Documentation/ROADMAP.md](Documentation/ROADMAP.md) for the phase plan.

---

## Getting started

1. Install **Unity 6000.5 LTS** (6000.0 LTS also works) via Unity Hub.
   If you use a different version, either let Unity upgrade the project when it opens,
   or edit `ProjectSettings/ProjectVersion.txt` to match your installed version first.
   `Packages/manifest.json` must stay in the repository — see
   [SETUP.md](Documentation/SETUP.md#required-packages--do-not-delete-packagesmanifestjson).
2. In Unity Hub choose **Add ▸ Add project from disk** and select this folder.
3. Open the project. The first import takes a few minutes.
4. In the menu bar run **PSM2 ▸ Setup ▸ Build or Rebuild Project**.
   This generates the folders, materials, player prefab and all five scenes.
5. Press **Play**.

Full instructions, including what to do if TextMeshPro prompts for resources, are in
[Documentation/SETUP.md](Documentation/SETUP.md).

## Controls

| Action | Key |
|---|---|
| Move | `W A S D` / arrow keys |
| Look | Mouse |
| Zoom | Mouse wheel |
| Jump | `Space` |
| Pause | `Esc` |
| Interact with a puzzle station | `E` |
| Answer a structure puzzle | Left click the structure, or drag the label onto it |
| Close a puzzle without answering | `Esc` |

## Documentation

- [SETUP.md](Documentation/SETUP.md) — opening, generating and building the project
- [ARCHITECTURE.md](Documentation/ARCHITECTURE.md) — systems, data flow, script responsibilities
- [ROADMAP.md](Documentation/ROADMAP.md) — the ten development phases
- [TESTING.md](Documentation/TESTING.md) — white-box test procedures for what exists today
