-- nvim --headless --clean -l nvim/attach_tcp.lua FILE...
-- Env: KFS_PORT (the server's "LISTENING <port>"), KFS_HOVER (file for hover/completion), KFS_RAPID=1
local here = vim.fs.dirname(vim.fs.abspath(debug.getinfo(1, 'S').source:sub(2)))
local common = dofile(here .. '/common.lua')
local port = assert(tonumber(os.getenv('KFS_PORT')), 'set KFS_PORT')
common.run({
  cmd = vim.lsp.rpc.connect('127.0.0.1', port),
  files = { unpack(_G.arg) },
  hover = os.getenv('KFS_HOVER'),
  rapid = os.getenv('KFS_RAPID') == '1',
})
