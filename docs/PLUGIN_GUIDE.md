# Plugin Developer Guide

RetroArr supports community-driven plugins for metadata enrichment and custom scripting. Plugins run as isolated external processes — a broken plugin can never crash the main application.

## Architecture

- Plugins live in the `config/plugins/` directory (one folder per plugin)
- Each plugin folder must contain a `plugin.json` manifest
- Plugin logic is implemented as any executable (Python, Bash, Node.js, etc.)
- Communication: JSON on stdin → JSON on stdout
- Fault isolation: process boundary + timeout + circuit breaker

## Plugin Manifest (`plugin.json`)

```json
{
  "name": "my-plugin",
  "version": "1.0.0",
  "apiVersion": "1",
  "description": "What this plugin does",
  "author": "Your Name",
  "type": "metadata",
  "command": "python3",
  "args": "main.py",
  "permissions": ["filesystem:read"],
  "enabled": true,
  "timeoutSeconds": 10
}
```

### Required Fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Unique plugin identifier |
| `apiVersion` | string | Must be `"1"` (current supported version) |
| `type` | string | `"metadata"` or `"script"` |
| `command` | string | Executable to run (resolved relative to plugin dir, or absolute) |

### Optional Fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `version` | string | `"1.0.0"` | Plugin version |
| `description` | string | `""` | Human-readable description |
| `author` | string | `""` | Plugin author |
| `args` | string | `""` | Arguments passed to the command |
| `permissions` | string[] | `[]` | Required permissions |
| `enabled` | bool | `true` | Whether the plugin is active |
| `timeoutSeconds` | int | `10` | Max execution time (1-300) |

### Permissions

| Permission | Description |
|-----------|-------------|
| `filesystem:read` | Plugin reads files from the game library |
| `filesystem:write` | Plugin writes files (use with caution) |
| `network:http` | Plugin makes HTTP requests to external services |

## Plugin Types

### Metadata Plugin (`type: "metadata"`)

Receives game info on stdin, returns supplemental metadata on stdout.

**Input (stdin):**
```json
{
  "title": "Super Mario Bros",
  "platform": "NES",
  "igdbId": 1234
}
```

**Output (stdout):**
```json
{
  "source": "my-metadata-plugin",
  "tags": ["platformer", "classic"],
  "notes": "Additional info about the game"
}
```

### Script Plugin (`type: "script"`)

Receives event/context data on stdin, returns processing results on stdout.

**Input (stdin):**
```json
{
  "event": "game_added",
  "files": ["game.nsp", "update.nsp"],
  "title": "Game Title"
}
```

**Output (stdout):**
```json
{
  "source": "my-script-plugin",
  "suggestedTags": ["Nintendo Switch", "NSP"],
  "fileCount": 2
}
```

## Fault Isolation

RetroArr ensures plugins cannot harm the main application:

1. **Process Boundary** — Each plugin runs as a separate OS process
2. **Timeout** — Plugins that exceed `timeoutSeconds` are killed
3. **Circuit Breaker** — After 3 consecutive failures, the plugin is disabled until manually reset
4. **Exit Code** — Non-zero exit codes are treated as failures

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/v3/plugins` | List all plugins and their status |
| `POST` | `/api/v3/plugins/{name}/execute` | Execute a plugin with JSON body as input |
| `POST` | `/api/v3/plugins/{name}/reset` | Reset circuit breaker for a plugin |
| `POST` | `/api/v3/plugins/reload` | Reload all plugins from disk |

## Creating Your First Plugin

1. Create a folder in `config/plugins/my-plugin/`
2. Add a `plugin.json` manifest (see format above)
3. Add your script (e.g., `main.py`)
4. Make sure the script is executable (`chmod +x main.py` on Linux)
5. Test via the API: `POST /api/v3/plugins/my-plugin/execute`

## Example Plugins

RetroArr ships with example plugins in `config.example/plugins/`:

- **example-metadata-plugin** — Returns supplemental metadata for a game
- **example-script-plugin** — Auto-tags games based on file extensions
- **bad-plugin-fixture** — Intentionally broken; proves the app stays up when a plugin hangs

Copy these to your `config/plugins/` directory to try them out.

## Troubleshooting

- Check plugin status via `GET /api/v3/plugins` — look for `isValid` and `error` fields
- If a plugin's circuit breaker is open, reset it via `POST /api/v3/plugins/{name}/reset`
- Plugin stdout/stderr is captured — check the `output` and `error` fields in the execution response
- Ensure your script reads from stdin (even if it ignores the input) and writes valid JSON to stdout
