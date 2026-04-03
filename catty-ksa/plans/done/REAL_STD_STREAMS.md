# Overview

## Current state

We have an existing custom game shell for the game KSA that simulates a subset of bash job and stream capabilities (stdin/stdout/stderr between faux programs), but it's not good enough. This concept is simply too complex to replicate faithfully in a limited manner, we'd have to reimplement the whole bash capabilities to make that work and there's just too much depth with filesystems and POSIX capability considerations.

We also have a fully working PTY for real OS shells that work 100% already and supports PowerShell, Windows Command Prompt, and Linux shells like WSL2.

The terminal emulator runs in-band in the game code so it can invoke game code directly.

We already have an RPC mechanism setup with private terminal escape sequences.  However right now it's largely unidirectional, the userland shell programs can emit escape sequences which the emulator is listening for and invokes game code.  We have not yet implemented a data flow from emulator to userland process using private escape sequences or some other mechanism.

## Desired end state

Working backwards from what end-state and capabilities I would like to achieve, conceptually, I want to be able to do something like these pseudo examples:

```bash
# list all crafts, filter where the name contains "rocket" and then ignite the engine of each
ksa-list-crafts | grep rocket | xargs -n 1 ksa-ignite-engine

# warp the current craft to jupiter in atmosphere with a velocity of 100m/s
ksa-warp --craft $(ksa-list-crafts --current) --to-celestial jupiter --situation atmosphere --speed "100m/s"
```

Where

* `ksa-list-crafts` - game code lists all crafts in the current game on separate lines. GAME > USERLAND unidirectional dataflow.
* `ksa-ignite-engine` - game code ignites the engine of a craft.  USERLAND > GAME unidirectional dataflow.
* `ksa-warp` - game code moves the supplied craft based on parameter values. USERLAND > GAME unidirectional dataflow.

## Current RPC

The USERLAND > GAME RPC flow works right now and is simple.  Userland programs simply emit private terminal escape sequences, we have a catch-call OSC private terminal sequence using code 1010 which can encode arbitrary JSON payloads so we can leverage the single private escape sequence and have an infinite number of complex datastructures passed to game code as an API mechanism.

For example, we support these now:

```bash
echo -ne '\e]1010;{"action":"engine_ignite"}\a'
echo -ne '\e]1010;{"action":"engine_shutdown"}\a'
```

We don't have a solution for sending data from GAME > USERLAND yet.

## GAME > USERLAND issues

I am under the impression this direction of data flow given our execution environment is harder to achieve.

The goal is to leverage USERLAND programs using real-world code in any programming or scripting language and leverage standard protocols and patterns available to us in the context of:

* Terminal emulator running in-band in game code
* Terminal emulator hosts a PTY which launches an OS shell running userland code and connects stdin/stdout/stderr data pumping
* Terminal emulator displays the visual terminal app in game code
* The userland code runs natively on the OS shell, NOT in-band in the game
* Thus, userland code cannot DIRECTLY access game code, and we need an RPC mechanism
* USERLAND > GAME data flow is solved with private terminal sequences
* GAME > USERLAND is not solved

### Potential solutions for GAME > USERLAND data flow

#### USERLAND program stdin stream

Just have game code write to the PTY stdin stream data stream? Would that work?

The complication is that I envision the RPC for a request-reply must be async.

For example the USERLAND code would emit an RPC call to "list-crafts", game code would execute to get the data to respond, and now it must write the data back to the userland process somehow.

Given our constraints, it makes sense to me that the userland program could listen on its stdin stream and the game could could write the data to the appropriate stream of the PTY to push it there.

But practically speaking, how would the USERLAND program implement that?

I'm thinking something like this (pseudo code with explanations):

```typescript
// list-crafts.ts USERLAND program

// fire-and-forget send private term escape OSC sequence to game code.  game code will find craft names then respond by pumping the data to the terminal session stdin stream
send_rpc_to_game({action: "list-crafts", correlation_id: "id123" }); 

// wait for stdin stream to receive data for correlation_id=id123, wait up to 1000ms before giving up
const crafts = await listen_for_data(correlation_id="id123", timeout=1000);
```

Does this have complications with multiple things using the stdin stream?

Or can we just assume only one program in USERLAND is essentially the foreground process and solely responsible for consuming data from stdin, so when we send a private term escape RPC it's simple enough for that same foreground process to listen for the expected response data on stdin?

If this is the case do we care about a correlation_id mechanism at all?

How do we know that the foreground USERLAND program has fully consumed the response data from the stdin stream?  Maybe our GAME > USERLAND data flow can be implemented where we fully buffer the data before writing it to the stdin stream and use a fixed-size header structure which can contain key metadata like the number of bytes that follow in the response.

I'm OK with fully buffering data to simplify things like this because in the context of our game we're only dealing with relatively small amounts of data at all times (in max of kilobytes of data only, should never be megabytes or larger).

I would want to be able to build up abstractions in userland code (ideally TypeScript with a bun runtime) which would make composition of bi-directional RPC invocations very simple and fast to code and execute.

