# Multiplayer readiness assessment

## Recommendation

Keep multiplayer transport deferred. The simulation has a useful fixed-step foundation, but the game does not yet have a clean boundary between player commands and mutable world state. A server-authoritative prototype is the best first multiplayer model once that boundary exists. Lockstep peer simulation should wait until cross-runtime determinism has been demonstrated.

This assessment completes the roadmap evaluation; it does not claim that Voidworks currently supports multiplayer.

## Current architecture

- `SimulationState.Step` advances the whole world in 0.1-second ticks. Headless runs can now save and resume at exact tick counts.
- `SimulationState` owns mutable world state and invokes power, plumbing, machinery, logistics, atmosphere, crew AI, and scenario updates in a fixed sequence.
- Crew AI runs sequentially in the `Crew` list. Earlier crew can change jobs, items, doors, and machine state before later crew run during the same tick. Changing update order could therefore change outcomes.
- The engine and inspection UI directly change simulation objects. For example, the panel adds and removes cable and pipe coordinates and posts jobs, while the app toggles pause state. There is no shared command queue or validation boundary for those actions.
- `SimulationSerializer` stores a versioned whole-world snapshot for persistence. It is not a network protocol, an incremental change stream, or a player-action replay log.
- Machine, crew, item, and job identities are generated with `Guid.NewGuid()`. They are preserved in saves, but new-world identity assignment is not a seeded network identity scheme.
- No networking or multiplayer transport code is present in the current source tree.

## Main risks

1. **Command ownership:** UI actions mutate the host simulation directly. Network clients need to submit requests that the authoritative simulation validates and applies at known tick boundaries.
2. **Order dependence:** sequential crew and machine processing may produce different outcomes if hosts apply commands or iterate actors in different orders.
3. **Determinism scope:** fixed ticks and repeatable scenario setup help local reproduction, but floating-point and runtime behavior have not been verified for lockstep across machines.
4. **Replication shape:** a persistence snapshot is useful for initial join and recovery, but multiplayer also needs bounded snapshots or deltas, sequence numbers, stale-command handling, and disconnect recovery.
5. **Pause and time controls:** pause and time-scale behavior needs an explicit authority policy before multiple players can control one simulation.

## Suggested maturity gates

1. Route all player-facing world mutations through typed simulation commands, such as construction orders, infrastructure placement, machine controls, and pause requests.
2. Apply commands in a stable order at a fixed tick boundary; record the tick, actor identity, and result for diagnostics and reproducible replay.
3. Define stable simulation identities and validate command ownership and target legality in the simulation layer.
4. Add replay comparisons and state invariants to the automated suite, including save/resume equivalence for representative scenarios.
5. Prototype one host and one client with host-authoritative simulation, an initial full snapshot, and periodic state updates. Start without rollback or peer lockstep.
6. Evaluate latency, late joins, disconnect/reconnect, host pause, and save/load recovery before expanding player count or networking scope.

Revisit multiplayer after the command boundary and save/replay guarantees are in place. Until then, keep the simulation single-authority and transport-free.
