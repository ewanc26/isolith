# Godot MCP

Isolith is manipulated by agents through a **Godot MCP server**, the same
integration Hollowfall uses. The server lets an agent run and stop the project,
read the debug output, inspect project metadata, and create or edit scenes and
nodes without a GUI.

The server is **Coding-Solo's `godot-mcp`**
([github.com/Coding-Solo/godot-mcp](https://github.com/Coding-Solo/godot-mcp)),
pinned to `0.1.1`. It needs no Godot plugin, GDExtension, or GDScript, so it does
not touch the project's language policy.

## Project declaration

[`.mcp.json`](../.mcp.json) is the project-level declaration:

```json
{
  "mcpServers": {
    "godot": {
      "command": "npx",
      "args": ["-y", "@coding-solo/godot-mcp@0.1.1"],
      "env": {
        "GODOT_PATH": "${GODOT_PATH:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
      }
    }
  }
}
```

The repository root is the Godot project directory: `project.godot` lives there,
so every tool that takes a project path takes the root.

## OpenCode configuration

The server is registered in the OpenCode config
(`~/.config/opencode/opencode.json`). Because the `godot` server may already be
running for another project, keep one entry and pass `projectPath` per call, or
run a second Isolith-specific server under a different name:

```json
{
  "mcp": {
    "servers": {
      "godot": {
        "type": "local",
        "command": ["/opt/homebrew/bin/godot-mcp"],
        "cwd": "/path/to/isolith",
        "environment": {
          "GODOT_PATH": "/Applications/Godot_mono.app/Contents/MacOS/Godot"
        },
        "enabled": true,
        "timeout": 30000
      }
    }
  }
}
```

`cwd` is a default, not a scope: `run_project`, `get_project_info`,
`create_scene`, `save_scene`, and the rest accept an explicit `projectPath`, so
one server can service both projects.

Restart OpenCode after changing the configuration; the server reconnects on
start.

## Available tools

| Group | Tools |
| --- | --- |
| Execution | `launch_editor`, `run_project`, `stop_project`, `get_debug_output` |
| Project info | `get_godot_version`, `get_project_info`, `list_projects` |
| Scenes | `create_scene`, `add_node`, `save_scene`, `load_sprite`, `export_mesh_library` |
| UIDs | `get_uid`, `update_project_uids` |

In OpenCode's Code Mode they are `tools.godot.*`.

## Running the game

```js
await tools.godot.run_project({ projectPath: "/path/to/isolith" });
await tools.godot.get_debug_output();
await tools.godot.stop_project();
```

Running the project executes real code, so the server's tools are **not**
auto-approved.

## Headless checks

The MCP is a convenience, not the source of truth. The authoritative check is
still the smoke test, which is what CI runs:

```bash
dotnet build
godot --headless --path . res://scenes/Smoke.tscn
```

Use the MCP to *see* debug output while iterating; use the smoke test to *prove*
the change.

## Troubleshooting

- **Server will not start** — confirm `which godot-mcp` resolves, or let `npx`
  fetch the pinned package. Confirm the Godot Mono binary exists at `GODOT_PATH`.
- **Tools time out** — raise `timeout` in the OpenCode config. The first `npx`
  run downloads the package and can be slow.
- **Stale Godot process** — `pkill -f "godot-mcp"` and restart OpenCode.
- **Wrong project** — pass `projectPath` explicitly rather than relying on the
  server's `cwd`.
