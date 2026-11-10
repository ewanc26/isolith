# Changelog

Isolith versions follow the project's single source of truth in
[`isolith.csproj`](isolith.csproj) (`<Version>`). Entries are grouped by
release, newest first.

## 0.2.0 (unreleased)

Platforming surface vocabulary and accessibility for the feedback it produces.

- `Ice` block kind: near-frictionless solid — momentum carries, steering is
  mushy, and dash plus ice composes into long slides.
- `Conveyor` block kind: collision that drags the player along its `flow` vector
  (metres per second). Route one into ice and it keeps feeding velocity the
  player can barely fight.
- Two new authored courses — **Frostline** and **Slipstream** — built on those
  two surfaces, alongside the existing movers, crumble platforms, and bounce
  pads.
- New setting: **camera shake** (0–1). Landing thuds, deaths, bounces, and
  dashes can be dialled back or stilled entirely without touching gameplay or
  run validity.
- Zoom controls and the settings slider clamp into one shared range, verified
  by the smoke test.

## 0.1.0

The game as first promoted: endless mode with the adaptive director, three
authored courses (The Ascent, The Abyss, Zenith), dash, the title screen and
course selection flow, local run history, and the optional AT Protocol stat
sync through libwolfram.
