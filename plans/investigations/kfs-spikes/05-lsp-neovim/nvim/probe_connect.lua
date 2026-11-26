-- nvim --headless --clean -l nvim/probe_connect.lua SAMPLES_DIR
-- Probes vim.lsp.rpc.connect semantics that matter for a server living inside the game process:
-- nothing listening, one client per root, server death, server restart on the same port.
-- Env: KFS_LSP_DLL (the server, spawned and killed by this script), KSA_DLL_DIR (inherited).
local here = vim.fs.dirname(vim.fs.abspath(debug.getinfo(1, 'S').source:sub(2)))
local common = dofile(here .. '/common.lua')
local out = common.out
local dll = assert(os.getenv('KFS_LSP_DLL'), 'set KFS_LSP_DLL')
local samples = vim.fs.abspath(assert(_G.arg[1], 'pass the samples dir'))

common.setup_filetype()
vim.notify = function(msg, lvl) out(string.format('NOTIFY[%s] %s', tostring(lvl), msg)) end

local function free_port()
  local t = vim.uv.new_tcp()
  t:bind('127.0.0.1', 0)
  local p = t:getsockname().port
  t:close()
  return p
end
local port = free_port()

local function name_of(buf) return vim.fs.basename(vim.api.nvim_buf_get_name(buf)) end
vim.api.nvim_create_autocmd('LspAttach', { callback = function(ev) out(('EVENT LspAttach client=%d buf=%s'):format(ev.data.client_id, name_of(ev.buf))) end })
vim.api.nvim_create_autocmd('LspDetach', { callback = function(ev) out(('EVENT LspDetach client=%d buf=%s'):format(ev.data.client_id, name_of(ev.buf))) end })
common.install_autocmd(vim.lsp.rpc.connect('127.0.0.1', port), {
  on_exit = function(code, signal, id) out(('EVENT on_exit client=%d code=%s signal=%s'):format(id, tostring(code), tostring(signal))) end,
})

-- get_clients() (what users see) vs every id vim.lsp.start() returned (incl. clients never initialized).
local function clients()
  local l = {}
  for _, c in ipairs(vim.lsp.get_clients({ name = 'kfs' })) do
    table.insert(l, ('#%d(init=%s,root=%s,bufs=%d)'):format(c.id, tostring(c.initialized),
      vim.fs.basename(c.root_dir or '?'), vim.tbl_count(c.attached_buffers)))
  end
  local s = {}
  for _, id in ipairs(common.started) do
    local c = vim.lsp.get_client_by_id(id)
    table.insert(s, ('#%d:%s'):format(id, c == nil and 'gone' or (c:is_stopped() and 'stopped' or (c.initialized and 'ready' or 'NOT-initialized'))))
  end
  return ('get_clients=%d [%s]; started ids [%s]'):format(#l, table.concat(l, ' '), table.concat(s, ' '))
end

local server, server_log = nil, {}
local function start_server()
  local listening = false
  server = vim.system({ 'dotnet', dll, '--listen', '127.0.0.1:' .. port }, {
    stdout = function(_, d) if d and d:match('LISTENING') then listening = true end end,
    stderr = function(_, d) if d then table.insert(server_log, d) end end,
  })
  assert(vim.wait(20000, function() return listening end, 20), 'server did not start')
end
local function stop_server()
  server:kill('sigterm')
  server:wait(5000)
end
local function edit(rel)
  vim.cmd.edit(vim.fn.fnameescape(samples .. '/' .. rel))
  return vim.api.nvim_get_current_buf()
end
local function sleep(ms) vim.wait(ms, function() return false end, 20) end

out('STEP 1: nothing listening on port ' .. port)
local b = edit('ok.c')
sleep(3000)
out('  after 3 s: clients=' .. clients())
start_server()
sleep(2000)
out('  server started on the same port; after 2 s: clients=' .. clients())
out('  buffer attached to client #1? ' .. tostring(vim.lsp.buf_is_attached(b, common.started[1] or -1)))
for _, id in ipairs(common.started) do
  local c = vim.lsp.get_client_by_id(id)
  if c then c:stop(true) end
end
sleep(500)
out('  force-stopped it: clients=' .. clients())

out('STEP 2: server up; re-edit ok.c, then error.c (same dir), then nasty/empty.c (other dir)')
vim.cmd('edit!')
common.wait_publish(vim.uri_from_bufnr(b), 1)
out('  ok.c: clients=' .. clients())
local b2 = edit('error.c')
common.wait_publish(vim.uri_from_bufnr(b2), 1)
out('  +error.c: clients=' .. clients())
local b3 = edit('nasty/empty.c')
common.wait_publish(vim.uri_from_bufnr(b3), 1)
out('  +nasty/empty.c: clients=' .. clients())

out('STEP 3: kill the server (game quits / mod reload)')
stop_server()
sleep(2000)
out('  after 2 s: clients=' .. clients() .. '; diagnostics still shown for error.c: ' .. #vim.diagnostic.get(b2))

out('STEP 4: restart the server on the same port')
start_server()
sleep(3000)
out('  after 3 s, no action: clients=' .. clients())
vim.cmd.buffer(b2)
vim.cmd('edit!')
common.wait_publish(vim.uri_from_bufnr(b2), 2, 10000)
out('  after :edit! of error.c: clients=' .. clients())

for _, c in ipairs(vim.lsp.get_clients({ name = 'kfs' })) do c:stop() end
sleep(1000)
stop_server()
for _, l in ipairs(vim.split(table.concat(server_log), '\n')) do
  if l:match('client #') or l:match('listening') then out('SERVER ' .. l) end
end
