# @ksa/tools - KSA CLI Tools

Example command-line tools for interacting with the Kitten Space Agency game engine via RPC.

## Installation

```bash
cd userland/ksa-tools
bun install

# Optional: Link globally to use commands from anywhere
bun link

# Then in your terminal session or profile, ensure KSA_RPC_SOCKET is set:
export KSA_RPC_SOCKET=/path/to/ksa.sock
```

**Important:** These are TypeScript files that run directly with Bun. No build step required!

## Requirements

- The KSA game must be running with RPC socket server enabled
- `KSA_RPC_SOCKET` environment variable must point to the socket file
- Bun runtime must be installed

## Commands

### ksa-list-crafts

List all craft vehicles in the game.

**Usage:**

```bash
# List all craft names (one per line)
ksa-list-crafts

# Output as JSON array
ksa-list-crafts --json

# Show only the current craft name
ksa-list-crafts --current
```

**Examples:**

```bash
# Find rockets
ksa-list-crafts | grep -i rocket

# Get the first craft
ksa-list-crafts | head -n1

# Count total crafts
ksa-list-crafts | wc -l

# Process JSON with jq
ksa-list-crafts --json | jq '.[].name'
```

### ksa-current-craft

Get the currently controlled craft.

**Usage:**

```bash
# Print craft name (empty if none controlled)
ksa-current-craft

# Output as JSON object
ksa-current-craft --json
```

**Examples:**

```bash
# Store in variable
current=$(ksa-current-craft)

# Use in conditional
if [ -n "$(ksa-current-craft)" ]; then
  echo "You are controlling a craft"
fi

# Process with xargs
ksa-current-craft | xargs -I{} echo "Controlling: {}"

# JSON output with jq
ksa-current-craft --json | jq '.id'
```

## Pipeline Examples

These tools are designed to work seamlessly in Unix pipelines:

```bash
# Find and display rocket crafts
ksa-list-crafts | grep rocket

# Get first available rocket
rocket=$(ksa-list-crafts | grep -i rocket | head -n1)

# Check if current craft is a rocket
ksa-current-craft | grep -i rocket && echo "Flying a rocket!"

# Complex pipeline: find non-controlled crafts
ksa-list-crafts --json | jq '.[] | select(.isControlled == false) | .name'
```

## Development

### Running directly (without linking)

```bash
# Run from anywhere
bun run userland/ksa-tools/bin/ksa-list-crafts.ts

# Or with execute permissions
chmod +x userland/ksa-tools/bin/*.ts
./userland/ksa-tools/bin/ksa-list-crafts.ts
```

### Error Handling

All commands:
- Exit with code 0 on success
- Exit with code 1 on error
- Print errors to stderr
- Print data to stdout (safe for piping)

### Adding New Commands

1. Create new `.ts` file in `bin/` with shebang: `#!/usr/bin/env bun`
2. Import from `@ksa/rpc-client`
3. Add entry to `package.json` bin section
4. Use `client.request()` to call RPC actions
5. Handle errors and exit codes appropriately
6. Make output pipeline-friendly

## Architecture

These tools use the `@ksa/rpc-client` package to communicate with the KSA game engine:

```
CLI Tool (TypeScript)
    ↓
@ksa/rpc-client (Bun SDK)
    ↓
Unix Domain Socket
    ↓
KSA Game Engine (C#)
```

The socket path is discovered automatically from the `KSA_RPC_SOCKET` environment variable, which is set by the caTTY terminal emulator when running inside the game.
