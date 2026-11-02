# Vertical Slice Playtest

Primary scenario: `derelict-crisis` (launched by default when no scenario or save is supplied).

## Pass criteria

A first-time player can, using only the normal UI:

1. Read the opening briefing and understand the immediate problem.
2. Identify the severed coolant loop and the offline oxygen generator.
3. Restore the coolant loop and power its external radiator pump.
4. Restore oxygen generation and keep it powered.
5. Build the auxiliary battery using construction orders and crew hauling.
6. Reach a mission-complete or mission-failed report without developer panels or console commands.
7. Recover from failure by loading an earlier save or by continuing to observe the aftermath.

**Status:** automated command-layer completion is covered by `VerticalSlicePlaythroughTests`.
The fresh-start mouse-only run and independent new-player test remain manual validation items.

The run should take roughly 10-20 minutes at 1x-5x speed. The clock is paused while the briefing is open.

## Scripted smoke path

1. Launch `dotnet run --project src/Voidworks.Game/Voidworks.Game.csproj`.
2. Confirm the **Mission Briefing** window appears, the simulation is paused, and four objectives plus first priorities are listed.
3. Press **BEGIN OPERATIONS**.
4. Select tile `(15, 2)` and press **Lay Coolant Pipe**.
5. Select tile `(16, 2)`, press **Lay Coolant Pipe**, then press **Lay Power Cable** on the same hull tile to start the external radiator pump.
6. The coolant objective should complete and the reactor temperature should fall below 500 K.
7. Select the oxygen generator at `(2, 5)` and press **Turn On**. Keep the reactor and battery online so it stays powered.
8. Select a free engineering floor tile and press **Build Battery (4 steel)**. Materials begin at `(3, 4)`.
9. Confirm the objective tracker updates, the mission report appears, and the alert feed records the mission result.
10. Save, load the save, and confirm the briefing/outcome does not repeat incorrectly and scenario progress survives.

## What to watch for

- Middle-click drag over any panel must not pan the world.
- Left-clicking a panel must not change the selected tile.
- Zooming must keep the tile under the cursor stable at 100%, 125%, and 150% Windows scaling.
- Resizing the window must keep the telemetry bar, toolbar, and inspector from overlapping.
- Objective status lines should state the current temperature, crew health, loop state, or battery count.
- Failure reasons must be specific: SCRAM, time limit, or all crew lost.

## Manual run log

### 2026-11-02 — successful fresh-start UI run

- Mission: `derelict-crisis`
- Result: **MISSION COMPLETE at 185.5s**
- Objectives: 4/4 completed (`coolant-loop`, `thermal-stability`, `life-support`, `auxiliary-power`)
- Battery: queued and built at the selected tile `(13,4)`; saved state contains two batteries
- Utilities: coolant loop connected and radiator feed powered
- Save/load: save slot 1 reloaded with the mission-complete report, 2 batteries, 5 coolant pipes, and 26 cables
- Confusion point: loading a completed save immediately shows the mission report, which initially looked unexpected. This is intended terminal-state behavior; **CONTINUE OBSERVING** and **STAY PAUSED** return to the ship.
- Minor issue: an accidental coolant pipe was placed at `(2,1)` during the run. It did not affect completion, but drag placement should still be reviewed for accidental strokes.
- Independent new-player test: not yet completed without step-by-step developer guidance.

## Confusion log

Record each point where the player hesitates or takes a wrong action.

| Time | Player action | Expected result | Actual result | Cause / suggested fix |
| --- | --- | --- | --- | --- |
| 2026-11-02 | Clicked **Load Ship** after saving a completed mission | Resume the ship view | Mission report appeared | Expected terminal-state behavior; add a clearer “loaded completed mission” note in the report |

## Known non-goals for this slice

- No campaign progression, contracts, or multiple ships.
- No manual crew movement or direct job assignment beyond existing priorities.
- Debug actions (breach injection, emergency patch) are hidden unless the Debug panel is enabled.
