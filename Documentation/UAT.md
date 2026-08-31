# User Acceptance Testing — preparation

Phase 10 deliverable. This is the protocol and the data plumbing for the PSM2
evaluation. It does **not** contain results: nobody has played the game yet.

## 1. What is being evaluated

PSM1's research contribution is the claim that **rule-based Dynamic Difficulty
Adjustment improves the learning experience** in a cardiovascular anatomy game. The
evaluation therefore has to compare two things:

| Half | Source | Status |
|---|---|---|
| What players **did** | `SessionRecord` history, exported as CSV | Collected automatically |
| What players **said** | Questionnaire + interview | Needs participants |

The objective half is what `SessionData` and `LevelPerformance` were shaped for from
Phase 1 onwards, and it is now exportable. The subjective half is the part this
document prepares.

## 2. Collecting the objective data

Each machine writes to
`%USERPROFILE%\AppData\LocalLow\PSM2 FYP\Cardiovascular Exploration\psm2_progress.json`.

After a participant finishes:

1. `PSM2 ▸ UAT ▸ Export Session Metrics to CSV`
2. Rename `psm2_uat_metrics.csv` to the participant id
3. Reset before the next participant: **Settings ▸ Reset Local Progress**, or delete
   the save file

> **Reset between participants.** The history is capped at 20 attempts and is *not*
> cleared automatically. Skipping this step silently merges two participants into one
> file, and the merge is not detectable afterwards — the rows carry the display name,
> not a participant id.

One row per finished attempt:

| Column | Compare against |
|---|---|
| `accuracy`, `puzzles_correct`, `incorrect_answers` | perceived difficulty; "was it too hard / too easy" |
| `mean_response_seconds` | perceived pace and time pressure |
| `final_difficulty` | **whether the DDA moved them at all**, and whether they noticed |
| `hints_used` | whether help arrived when it was wanted |
| `puzzles_failed`, `completed` | frustration, and whether anyone got stuck |
| `duration_seconds` | engagement, and session-length planning |

## 3. Participant protocol

Target per PSM1: students who have covered cardiovascular anatomy. Aim for enough
participants that a difficulty-tier change is observable in more than one of them.

Per participant, roughly 40 minutes:

| Step | Time | Notes |
|---|---|---|
| Brief and consent | 5 min | Explain it is the game being tested, not them |
| Free play, Level 1 | 10 min | No guidance — the tutorial level has to teach itself |
| Levels 2 and 3 | 15 min | Level 2 carries the collateral-circulation moment (MR-1) |
| Questionnaire | 5 min | Section 4 below |
| Interview | 5 min | Section 5 below |
| Export CSV | 2 min | Section 2 above |

**Do not coach during play.** Where a participant gets stuck is data, and it is the
main thing the DDA is supposed to respond to. Note the timestamp instead.

## 4. Questionnaire

Five-point Likert unless stated. Each item names the metric it is meant to be read
against — an item that cannot be cross-checked against recorded behaviour is not
worth asking here.

**Learning**
1. I understand the path blood takes through the left ventricle. → `accuracy` on Level 1
2. I understand why the right ventricle wall is thinner than the left. → `lv3_mc_wall_thickness`
3. I understand what the Circle of Willis is for. → `lv2_mc_collateral`, and MR-1
4. The explanations after each answer helped me. → `incorrect_answers` trend within a level

**Difficulty and adaptation**
5. The questions were at about the right level for me. → `final_difficulty`, `accuracy`
6. The game got easier or harder as I played. → **did `final_difficulty` actually change**
7. When it changed, it changed for the right reason. → tier change vs `puzzles_failed` around it
8. I had enough attempts at each question. → `incorrect_answers`, `puzzles_failed`
9. Help arrived when I needed it, not before or after. → `hints_used`

**Experience**
10. I could tell what I was supposed to do next. → `duration_seconds`, stuck points
11. Moving and looking around felt comfortable. → TC-02 / TC-03 manual notes
12. The hostile cells made the game more tense rather than more annoying. → hostiles spawned vs killed
13. I would use this to revise. → overall

**Free text**
14. What was the most confusing moment?
15. What would you change first?

> **Item 6 is the load-bearing one.** If the recorded `final_difficulty` never moves,
> items 6 and 7 measure nothing, and the study cannot speak to the research question.
> Check the CSV for tier movement before treating the questionnaire as usable.

## 5. Interview prompts

Five minutes, semi-structured, after the questionnaire so it does not lead it.

1. Talk me through the moment you were most stuck. What did you try?
2. *(Level 2)* When the basilar artery was blocked — what did you think was happening,
   and what made you try another route? **(MR-1: this is the whole question)**
3. Did the game ever feel like it was adjusting to you? What made you think so?
4. Was anything on screen unclear or unreadable?
5. Anything that felt wrong anatomically?

## 6. Threats to validity, stated up front

Recorded honestly because they affect how the results can be read:

- **No human has played this build at all.** Every number in this project is from
  automated checks. The first participant is also the first player, so pilot with one
  person and fix what breaks before running the rest.
- **The DDA may not move for a competent player inside one session.** Promotion needs
  three resolved puzzles and a cooldown; a strong player on Level 1 might finish
  before a second change can occur. If tiers do not move, that is a finding about the
  tuning, not a failed study — report it as such.
- **`HintSource.Requested` is unreachable.** The hint button was removed, so
  questionnaire item 9 is about *automatic* and *earned* hints only.
- **Firebase is not integrated.** Data stays on the machine and must be collected by
  hand. There is no cross-device history and no account.
- **Levels 2 and 3 have never been seen by anyone.** They pass their navigability
  checks, but their pacing is entirely unvalidated — see MR-1 and MR-3 in TESTING.md.
- **Audio is placeholder tones.** Reactions to sound should not be read as reactions to
  designed audio.

## 7. Before the first participant

- [ ] Complete the MANUAL PLAYTEST CHECKLIST in [TESTING.md](TESTING.md) yourself
- [ ] Resolve MR-1, MR-2, MR-3
- [ ] Confirm ≥ 60 FPS on the machine participants will use, and record its specs
- [ ] Build a Windows executable and test it *as a build*, not in the editor
- [ ] Pilot with one person, then fix before continuing
