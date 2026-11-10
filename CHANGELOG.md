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
- `Vent` block kind: a rising wind column — catches a falling player, lifts
  them while inside, and caps the rise so a bounce pad dumped straight in buys
  nothing extra. Cross it on the belt's momentum or slide in off the ice.
- `Gust` block kind: a horizontal wind wall — pushes the player toward its
  `flow` frame at up to the wind's own speed, and leaves anything faster
  (a dash, a conveyor) untouched. Two gust walls join the Crosswinds course:
  one that carries a bounce's arc to its landing, one blowing sideways across
  the ice.
- Endless mode speaks the first surface: **ice** now appears in generated
  sections, gated by its own trust — die sliding off one and the director
  backs off ice specifically, cruise and it leans in with the difficulty.
  Never the section's first landing, like crumble, so a slide is never
  unreadable by construction.
- Three new authored courses — **Frostline**, **Slipstream**, and
  **Crosswinds** — built on those surfaces, alongside the existing movers,
  crumble platforms, and bounce pads.
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
