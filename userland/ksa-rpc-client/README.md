# @ksa/rpc-client

TypeScript/Bun client SDK for communicating with Kitten Space Agency (KSA) game engine via Unix domain sockets.

## Installation

```bash
# In your userland project directory
bun add file:../ksa-rpc-client
```

Or use directly without installation (Bun can run .ts files):

```bash
bun run path/to/ksa-rpc-client/src/index.ts
```

## Usage

### Basic Example

```typescript
import { KsaRpcClient } from "@ksa/rpc-client";

// Create client (uses KSA_RPC_SOCKET environment variable)
const client = new KsaRpcClient();

// List all crafts
const crafts = await client.call("list-crafts");
console.log("Available crafts:", crafts);

// Get current craft
const current = await client.call("get-current-craft");
console.log("Current craft:", current);
```

### Custom Socket Path

```typescript
const client = new KsaRpcClient("/tmp/ksa-rpc.sock");
```

### With Timeout

```typescript
const client = new KsaRpcClient(undefined, { timeout: 10000 }); // 10 second timeout
```

### Type-Safe Calls

```typescript
import { KsaRpcClient, type CraftInfo } from "@ksa/rpc-client";

const client = new KsaRpcClient();

// Get typed response
const crafts = await client.call<CraftInfo[]>("list-crafts");
crafts.forEach(craft => {
  console.log(`${craft.id}: ${craft.name}`);
});
```

### Error Handling

```typescript
try {
  const result = await client.call("ignite-engine", { craftId: 123 });
  console.log("Engine ignited:", result);
} catch (error) {
  console.error("Failed to ignite engine:", error.message);
}
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `KSA_RPC_SOCKET` | Path to Unix domain socket for RPC communication | (required if not passed to constructor) |

## Available Actions

| Action | Parameters | Returns | Description |
|--------|------------|---------|-------------|
| `list-crafts` | - | `CraftInfo[]` | List all available crafts |
| `get-current-craft` | - | `CraftInfo` | Get currently controlled craft |
| `ignite-engine` | `{ craftId: number }` | `boolean` | Ignite craft's main engine |
| `shutdown-engine` | `{ craftId: number }` | `boolean` | Shutdown craft's main engine |
| `get-throttle-status` | `{ craftId: number }` | `{ throttle: number }` | Get engine throttle percentage |

## API Reference

### `KsaRpcClient`

#### Constructor

```typescript
new KsaRpcClient(socketPath?: string, options?: ClientOptions)
```

- `socketPath`: Optional path to Unix domain socket (defaults to `KSA_RPC_SOCKET` env var)
- `options.timeout`: Request timeout in milliseconds (default: 5000)

#### Methods

##### `call<T>(action: string, params?: Record<string, unknown>): Promise<T>`

Call an RPC action on the game server.

- `action`: Action name to invoke
- `params`: Optional parameters for the action
- Returns: Promise resolving to typed response data
- Throws: Error if request fails or times out

##### `getSocketPath(): string`

Get the configured socket path.

## Running Scripts Directly

Since Bun can execute TypeScript directly, you can run scripts without a build step:

```bash
# Set the socket path
export KSA_RPC_SOCKET=/tmp/ksa-rpc.sock

# Run a script that uses the client
bun run my-script.ts
```

Example script (`my-script.ts`):

```typescript
#!/usr/bin/env bun
import { KsaRpcClient } from "@ksa/rpc-client";

const client = new KsaRpcClient();
const crafts = await client.call("list-crafts");
console.log(JSON.stringify(crafts, null, 2));
```

Make it executable:
```bash
chmod +x my-script.ts
./my-script.ts
```

## Integration with Shell Commands

This client enables powerful shell-based workflows:

```bash
# List all crafts and filter for rockets
ksa-list-crafts | grep rocket

# Ignite engine on a specific craft
ksa-list-crafts | grep "Saturn V" | ksa-ignite-engine

# Chain multiple commands
ksa-switch-to-craft $(ksa-list-crafts | grep rocket | head -1) && ksa-ignite-engine
```

See `userland/` directory for example command-line tools built with this client.

## Development

No build step required - Bun executes TypeScript directly!

```bash
# Type checking only (optional)
bun run tsc --noEmit

# Run tests (when implemented)
bun test
```

## License

MIT
