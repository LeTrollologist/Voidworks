# Changelog

All notable changes to Voidworks are recorded here. Versions follow the build
identity in `Directory.Build.props`; save files are versioned separately and
documented in [`docs/simulation-contract.md`](docs/simulation-contract.md).

## [0.1.0] - 2026-11-08

First tagged release: the `derelict-crisis` vertical slice with autonomous crew,
industrial production, life support, power, plumbing, and cascading failures.

### Added

- **Crew proficiency.** Every crew member has construction, repair, and logistics
  skill levels (1-10). Proficiency scales work speed for the matching discipline:
  a level-5 generalist is neutral, while specialists weld, build, and haul faster.
- **Crew work priorities.** Per-crew preferences (low, normal, high, urgent) for each
  discipline, adjustable from the management roster and crew inspector. Preferences
  bias which routine job a crew member claims first; emergencies and personal needs
  always take precedence.
- **`SetCrewWorkPriorityCommand`**, a validated command that records its outcome in
  the simulation event log.
- **Save format version 8**, persisting crew skills and work priorities. Versions 0-7
  still load; older saves use default skills and normal priorities.
- **Build identity.** Version metadata in `Directory.Build.props`, a `--version` flag,
  and the version in the window title and console banner.
- Release documentation: this changelog and updated README, roadmap, and simulation
  contract.

### Changed

- Emergency jobs (priority 100 and above) now sort ahead of all routine work
  explicitly, so a favored routine job can never outrank a breach repair.
- The management roster shows each crew member's skills and priority controls, and
  the crew inspector exposes the same controls.

### Notes

- 201 automated tests pass against this revision, including new coverage for skill
  work rates, priority-biased job selection, emergency precedence, command
  validation, save round-trips, and loading pre-version-8 saves.
- The graphical viewport requires OpenGL 4.1 core; headless modes run without it.
- No project license has been selected yet. Source reuse remains restricted until the
  repository includes a license.
