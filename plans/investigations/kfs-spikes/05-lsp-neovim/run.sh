#!/usr/bin/env bash
# Builds the kFS LSP spike and drives it from headless Neovim over TCP and stdio.
# Needs: KSA_DLL_DIR (folder holding KSA.dll), dotnet (net10.0), nvim >= 0.12 on PATH.
set -euo pipefail
cd "$(dirname "$0")"
: "${KSA_DLL_DIR:?set KSA_DLL_DIR to the folder holding KSA.dll}"
export KSA_DLL_DIR

# Keep Neovim's state (lsp.log) inside the spike folder; --clean skips the user's config and plugins.
mkdir -p out
export XDG_STATE_HOME="$PWD/out/xdg-state" XDG_CACHE_HOME="$PWD/out/xdg-cache" XDG_DATA_HOME="$PWD/out/xdg-data"
NVIM=(nvim --headless --clean -l)

echo "== build"
dotnet build KfsLspSpike -c Release -nologo -v q | tail -3
export KFS_LSP_DLL="$PWD/KfsLspSpike/bin/Release/net10.0/KfsLspSpike.dll"
SAMPLES=(samples/ok.c samples/error.c samples/unicode.c samples/header_error.c
         samples/nasty/unterminated_comment.c samples/nasty/unterminated_string.c
         samples/nasty/missing_include.c samples/nasty/empty.c samples/nasty/throws.c)

echo "== compiler checks (no LSP)"
dotnet "$KFS_LSP_DLL" --bench samples/ok.c samples/examples/*.c 2>/dev/null
dotnet "$KFS_LSP_DLL" --stress --cold samples/*.c samples/examples/*.c 2>/dev/null
dotnet "$KFS_LSP_DLL" --nasty samples/error.c samples/header_error.c samples/nasty/*.c 2>/dev/null

echo "== TCP: server --listen 127.0.0.1:0, Neovim connects with vim.lsp.rpc.connect"
: > out/tcp-server.out
dotnet "$KFS_LSP_DLL" --listen 127.0.0.1:0 >out/tcp-server.out 2>out/tcp-server.log &
SERVER_PID=$!
for _ in $(seq 1 200); do grep -q '^LISTENING ' out/tcp-server.out && break; sleep 0.1; done
PORT=$(awk '/^LISTENING /{print $2}' out/tcp-server.out)
[ -n "$PORT" ] || { echo "server did not start"; cat out/tcp-server.log; exit 1; }
echo "server pid $SERVER_PID listening on $PORT"
KFS_PORT=$PORT KFS_HOVER=samples/ok.c KFS_RAPID=1 "${NVIM[@]}" nvim/attach_tcp.lua "${SAMPLES[@]}"
echo "-- a second Neovim against the same running server (unicode.c only)"
KFS_PORT=$PORT "${NVIM[@]}" nvim/attach_tcp.lua samples/unicode.c
kill "$SERVER_PID"; wait "$SERVER_PID" 2>/dev/null || true
echo "-- server stderr (tcp)"; cat out/tcp-server.log

echo "== stdio: Neovim spawns 'dotnet KfsLspSpike.dll --stdio' (server forced to utf-8 positions)"
KFS_ENCODING=utf-8 KFS_HOVER=samples/ok.c KFS_RAPID=1 "${NVIM[@]}" nvim/attach_stdio.lua "${SAMPLES[@]}"
echo "-- stdio, server forced to utf-32 positions (unicode.c only)"
KFS_ENCODING=utf-32 "${NVIM[@]}" nvim/attach_stdio.lua samples/unicode.c

echo "== vim.lsp.rpc.connect semantics probe"
"${NVIM[@]}" nvim/probe_connect.lua samples
