-- nvim --headless --clean -l nvim/attach_stdio.lua FILE...
-- Env: KFS_LSP_DLL (path to KfsLspSpike.dll), KFS_ENCODING (optional: force utf-8|utf-16|utf-32 on the
-- server side), KFS_HOVER, KFS_RAPID=1. KSA_DLL_DIR is inherited by the spawned server.
local here = vim.fs.dirname(vim.fs.abspath(debug.getinfo(1, 'S').source:sub(2)))
local common = dofile(here .. '/common.lua')
local cmd = { 'dotnet', assert(os.getenv('KFS_LSP_DLL'), 'set KFS_LSP_DLL'), '--stdio' }
if os.getenv('KFS_ENCODING') then vim.list_extend(cmd, { '--encoding', os.getenv('KFS_ENCODING') }) end
common.run({
  cmd = cmd,
  files = { unpack(_G.arg) },
  hover = os.getenv('KFS_HOVER'),
  rapid = os.getenv('KFS_RAPID') == '1',
})
