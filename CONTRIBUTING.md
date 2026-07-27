# Contributing

Thanks for taking a look. [`AGENTS.md`](AGENTS.md) is the detailed architecture
guide — this file is just the practical bits.

## Setup

```bash
git clone https://github.com/ewanc26/isolith.git
cd isolith
dotnet build
godot --path .
```

Godot 4.7 (.NET build) and the .NET 10 SDK are required. `dotnet build` must run
before Godot can launch the project — Godot loads the compiled assembly, it does
not compile C# itself.

## Before opening a pull request

```bash
dotnet build                                       # must be warning-clean
godot --headless --path . res://scenes/Smoke.tscn  # must exit 0
python3 tools/generate_assets.py                   # must leave the tree clean
git status --porcelain                             # should be empty
```

CI runs exactly these.

## Conventions

- Conventional commits, scoped: `feat(gameplay):`, `fix(level):`, `docs(sync):`.
  Scopes: `build`, `core`, `level`, `gameplay`, `ui`, `sync`, `content`,
  `assets`, `test`, `ci`, `docs`.
- One logical change per commit.
- Nullable reference types are on. Don't suppress warnings to get a build
  through.
- Comment *why*, not *what*.

## Things worth knowing

- **All runtime code is C#.** GDScript is reserved for editor-only tooling under
  `addons/`, and Python is for repository tooling in `tools/` — never for
  anything the game loads or needs to build. See §2 of [`AGENTS.md`](AGENTS.md).
- **Assets must be generated or hand-authored as source text.** No imported art,
  audio, models, or fonts. See [`ASSETS.md`](ASSETS.md).
- **Sync must never be load-bearing.** The game plays identically without a
  network, an account, or `libwolfram`.
- **Interop is the sharp edge.** If you are touching `src/Sync/Interop/`, read
  §8 of `AGENTS.md` first — string ownership and struct layout there are not
  forgiving.
- **Changing player tuning changes level design.** Recompute the jump envelope
  in §6 of `AGENTS.md` and update the README.

## Levels

New courses are welcome. Drop a JSON file in `courses/`; the format is in §7 of
`AGENTS.md`. Run the smoke test — it will tell you if the spawn, a checkpoint,
or the goal ends up hanging over nothing.
