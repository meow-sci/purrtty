-- Test driver for the purrTTY kFS Neovim tests (purrTTY.Kfs.Tools.Tests, Runtime/Nvim/).
--
-- Loaded with `--cmd "lua dofile(vim.env.KFS_TEST_DRIVER)"` BEFORE the launcher's own --cmd, so it
-- sees everything the runtime files do from the start. It records messages (vim.notify and
-- nvim_echo, which it swallows: a passing test prints nothing), LspAttach/LspDetach events and
-- :Kfs* command results, sets vim.g.purrtty_kfs from KFS_TEST_OPTS, then, after VimEnter, runs
-- the scenario KFS_TEST_SCENARIO (a file returning function(T)) and writes what it returns as
-- JSON to KFS_TEST_RESULT before quitting. A Lua error becomes { error = traceback }.

local T = {}
_G.KfsTest = T

local uv = vim.uv
T.args = vim.json.decode(vim.env.KFS_TEST_ARGS or '{}', { luanil = { object = true, array = true } }) or {}
T.messages = {} -- { msg, level }
T.events = {} -- { kind = 'attach'|'detach', client, name, file }
T.done = {} -- KfsCommandDone data, in order
T.t0 = uv.hrtime()

local opts = vim.json.decode(vim.env.KFS_TEST_OPTS or '{}')
if type(opts) == 'table' and next(opts) then vim.g.purrtty_kfs = opts end

-- A Neovim that claims to be older than the runtime files support.
if T.args.pretend_old then
  local has = vim.fn.has
  vim.fn.has = function(feature)
    if type(feature) == 'string' and feature:match('^nvim%-0%.1[1-9]') then return 0 end
    return has(feature)
  end
end

vim.notify = function(msg, level)
  T.messages[#T.messages + 1] = { msg = tostring(msg), level = level or vim.log.levels.INFO }
end
vim.api.nvim_echo = function(chunks, _, o)
  local parts = {}
  for _, c in ipairs(chunks or {}) do parts[#parts + 1] = tostring(c[1]) end
  T.messages[#T.messages + 1] = { msg = table.concat(parts), level = (o and o.err) and vim.log.levels.ERROR or vim.log.levels.INFO, echo = true }
end

local function client_name(id)
  local c = vim.lsp.get_client_by_id(id)
  return c and c.name or '?'
end

vim.api.nvim_create_autocmd('LspAttach', { callback = function(ev)
  T.events[#T.events + 1] = { kind = 'attach', client = ev.data.client_id, name = client_name(ev.data.client_id), file = vim.fs.basename(vim.api.nvim_buf_get_name(ev.buf)) }
end })
vim.api.nvim_create_autocmd('LspDetach', { callback = function(ev)
  T.events[#T.events + 1] = { kind = 'detach', client = ev.data.client_id, file = vim.fs.basename(vim.api.nvim_buf_get_name(ev.buf)) }
end })
vim.api.nvim_create_autocmd('User', { pattern = 'KfsCommandDone', callback = function(ev)
  T.done[#T.done + 1] = ev.data
end })

-- ------------------------------------------------------------------ helpers

--- Waits for cond() (polling every 10 ms) or fails the scenario after timeout_ms (default 30 s).
function T.wait(what, cond, timeout_ms)
  if not vim.wait(timeout_ms or 30000, cond, 10) then error('timed out waiting for ' .. what, 2) end
end

--- Lets every callback queued so far run (a vim.schedule round trip).
function T.settle()
  local done = false
  vim.schedule(function() done = true end)
  T.wait('the event loop', function() return done end)
end

--- Tells the C# side `name` (it waits with NvimRun.WaitForSignal).
function T.signal(name)
  local f = assert(io.open(vim.env.KFS_TEST_SIGNALS .. '/' .. name, 'w'))
  f:close()
end

--- Waits until the C# side called NvimRun.Signal(name).
function T.wait_signal(name, timeout_ms)
  local path = vim.env.KFS_TEST_SIGNALS .. '/c-' .. name
  T.wait('signal ' .. name, function() return uv.fs_stat(path) ~= nil end, timeout_ms or 60000)
end

function T.edit(path)
  vim.cmd.edit(vim.fn.fnameescape(path))
  return vim.api.nvim_get_current_buf()
end

function T.kfs(buf)
  return vim.lsp.get_clients({ bufnr = buf, name = 'kfs' })[1]
end

--- Waits until buf has an initialized kfs client; returns it.
function T.attached(buf, what)
  T.wait(what or ('a kfs client for ' .. vim.api.nvim_buf_get_name(buf)), function()
    local c = T.kfs(buf)
    return c ~= nil and c.initialized
  end)
  return T.kfs(buf)
end

function T.diagnostics(buf)
  local out = {}
  for _, d in ipairs(vim.diagnostic.get(buf)) do
    out[#out + 1] = { lnum = d.lnum, col = d.col, end_col = d.end_col, code = d.code, severity = d.severity }
  end
  table.sort(out, function(a, b) return a.lnum < b.lnum or (a.lnum == b.lnum and a.col < b.col) end)
  return out
end

function T.wait_diagnostics(buf)
  T.wait('diagnostics for ' .. vim.api.nvim_buf_get_name(buf), function() return #vim.diagnostic.get(buf) > 0 end)
  return T.diagnostics(buf)
end

--- Messages containing `text` (plain).
function T.messages_with(text)
  local out = {}
  for _, m in ipairs(T.messages) do
    if m.msg:find(text, 1, true) then out[#out + 1] = m end
  end
  return out
end

--- Runs an Ex command that ends in a KfsCommandDone event and returns that event's data.
function T.command(ex)
  local n = #T.done
  vim.cmd(ex)
  T.wait('the result of ' .. ex, function() return #T.done > n end)
  return T.done[n + 1]
end

--- Sends a request through Neovim's own client for buf and returns its result (errors fail).
function T.request(buf, method, params)
  local res, err = vim.lsp.buf_request_sync(buf, method, params, 20000)
  if not res then error(method .. ': ' .. tostring(err), 2) end
  for _, x in pairs(res) do
    if x.error then error(method .. ': ' .. vim.inspect(x.error), 2) end
    return x.result
  end
end

--- Places the cursor of the current window on buf at 0-based (row, col) and returns the position params.
local function at(buf, row, col)
  vim.api.nvim_win_set_buf(0, buf)
  vim.api.nvim_win_set_cursor(0, { row + 1, col })
  return vim.lsp.util.make_position_params(0, T.kfs(buf).offset_encoding)
end

--- textDocument/hover at 0-based (row, col): the markdown/plain text, or nil.
function T.hover(buf, row, col)
  local result = T.request(buf, 'textDocument/hover', at(buf, row, col))
  if type(result) ~= 'table' then return nil end
  local c = result.contents
  return type(c) == 'table' and c.value or c
end

--- textDocument/completion at 0-based (row, col): the item labels.
function T.complete(buf, row, col)
  local result = T.request(buf, 'textDocument/completion', at(buf, row, col))
  local items = type(result) == 'table' and (result.items or result) or {}
  local out = {}
  for _, it in ipairs(items) do out[#out + 1] = it.label end
  return out
end

--- The diagnostic codes of buf, joined with ','.
function T.codes(buf)
  local out = {}
  for _, d in ipairs(T.diagnostics(buf)) do out[#out + 1] = tostring(d.code) end
  return table.concat(out, ',')
end

function T.lines(buf)
  return vim.api.nvim_buf_get_lines(buf, 0, -1, false)
end

function T.autocmd_count()
  local ok, list = pcall(vim.api.nvim_get_autocmds, { group = 'purrtty_kfs' })
  return ok and #list or 0
end

function T.user_commands()
  local out = {}
  for name in pairs(vim.api.nvim_get_commands({})) do
    if name:match('^Kfs') then out[#out + 1] = name end
  end
  table.sort(out)
  return out
end

local function write(tbl)
  local f = assert(io.open(vim.env.KFS_TEST_RESULT, 'w'))
  f:write(vim.json.encode(tbl))
  f:close()
end

-- A hard limit inside Neovim, so a hung scenario still reports what it saw.
local limit = uv.new_timer()
limit:start(80000, 0, vim.schedule_wrap(function()
  write({ error = 'the scenario did not finish within 80 s', messages = T.messages, events = T.events })
  vim.cmd('cquit 3')
end))

vim.api.nvim_create_autocmd('VimEnter', { once = true, callback = function()
  vim.schedule(function()
    local ok, res = xpcall(function()
      local scenario = dofile(vim.env.KFS_TEST_SCENARIO)
      return scenario(T)
    end, debug.traceback)
    limit:stop()
    if ok then
      res = res or {}
      res.messages = res.messages or T.messages
      res.events = res.events or T.events
      write(res)
    else
      write({ error = tostring(res), messages = T.messages, events = T.events })
    end
    vim.cmd('qa!')
  end)
end })
