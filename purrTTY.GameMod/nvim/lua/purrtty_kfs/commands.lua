-- purrTTY kFS: the :Kfs* commands.
--
-- Each one sends `workspace/executeCommand` to the buffer's kfs client; the result is always
-- { ok = bool, output = string, error = string|nil }. `ok` means the game RAN the command; what
-- it made of it (a build error, "kfs is off", ...) is in `output`, the game's own text, which is
-- shown verbatim and never called a success. `ok` false means it did not run (`error` says why).
-- :KfsBuild, :KfsLoad and :KfsVerb first write the modified buffers of the workspace (the game
-- reads the disk, never Neovim's unsaved text) -- not when the server has said it cannot run them.
-- When a command finishes, the User event KfsCommandDone fires with
-- data = { command = 'kfs.build', ok = bool, output = '...', error = '...' }.

local M = {}

local api = vim.api
local levels = vim.log.levels
local clean = require('purrtty_kfs.text')
local OUTPUT_NAME = 'kfs://output'
local output_buf = nil -- by id: vim.fn.bufnr(name) matches patterns and partial names

local function kfs()
  return require('purrtty_kfs')
end

local function notify(msg, level)
  kfs().notify(msg, level)
end

local function done(command, ok, output, err)
  api.nvim_exec_autocmds('User', {
    pattern = 'KfsCommandDone',
    modeline = false,
    data = { command = command, ok = ok, output = output, error = err },
  })
end

local SOURCE_EXT = { c = true, h = true, kasm = true, kinc = true }

local function is_source(name)
  local ext = name:match('%.([^./\\]+)$')
  return ext ~= nil and SOURCE_EXT[ext:lower()] == true
end

--- Writes every modified kFS source buffer below root record r (:update in each).
local function write_workspace(r)
  for _, b in ipairs(api.nvim_list_bufs()) do
    if api.nvim_buf_is_loaded(b) and vim.bo[b].modified and vim.bo[b].buftype == '' then
      local name = api.nvim_buf_get_name(b)
      local root = name ~= '' and is_source(name) and kfs().root_of(name)
      if root and root.keys[1] == r.keys[1] then
        local ok, err = pcall(api.nvim_buf_call, b, function() vim.cmd('silent update') end)
        if not ok then return false, ('could not write %s: %s'):format(vim.fn.fnamemodify(name, ':~:.'), tostring(err)) end
      end
    end
  end
  return true
end

--- Shows text in the kfs://output scratch buffer (a split, reused); every line cleaned
--- (purrtty_kfs.text).
function M.show_long(title, text)
  local b = output_buf
  if not (b and api.nvim_buf_is_valid(b) and api.nvim_buf_is_loaded(b)) then
    -- Gone, or deleted/unloaded by hand: a fresh one (any leftover of that name wiped first).
    for _, x in ipairs(api.nvim_list_bufs()) do
      if api.nvim_buf_get_name(x) == OUTPUT_NAME then pcall(api.nvim_buf_delete, x, { force = true }) end
    end
    b = api.nvim_create_buf(true, true)
    api.nvim_buf_set_name(b, OUTPUT_NAME)
    output_buf = b
  end
  vim.bo[b].buftype = 'nofile'
  vim.bo[b].bufhidden = 'hide'
  vim.bo[b].swapfile = false
  local lines = clean.lines(text)
  while #lines > 0 and lines[#lines] == '' do lines[#lines] = nil end
  table.insert(lines, 1, '-- ' .. clean.line(title))
  vim.bo[b].modifiable = true
  api.nvim_buf_set_lines(b, 0, -1, false, lines)
  vim.bo[b].modifiable = false
  vim.bo[b].modified = false
  local wins = vim.fn.win_findbuf(b)
  if #wins == 0 then
    local cur = api.nvim_get_current_win()
    vim.cmd('botright split')
    api.nvim_win_set_buf(0, b)
    api.nvim_win_set_height(0, math.min(#lines, math.max(5, math.floor(vim.o.lines / 2))))
    api.nvim_win_set_cursor(0, { 1, 0 })
    if api.nvim_win_is_valid(cur) then api.nvim_set_current_win(cur) end
  else
    api.nvim_win_set_cursor(wins[1], { 1, 0 })
  end
  return b
end

local function fits(line)
  local room = tonumber(vim.v.echospace) or 60
  return not line:find('\n', 1, true) and vim.fn.strdisplaywidth(line) <= math.max(40, room)
end

--- Shows a command result: a short text as a message, a long one in kfs://output. A command
--- that ran shows the game's text as it is (it may report an error: the client cannot tell);
--- one that did not run is a warning.
local function show(title, ok, output, err)
  output = (type(output) == 'string' and output or ''):gsub('%s+$', '')
  if ok then
    local one = not output:find('[\r\n]') and clean.line(output)
    if output == '' then
      notify(title .. ': sent; the game printed nothing')
    elseif one and fits('kFS: ' .. title .. ': ' .. one) then
      notify(title .. ': ' .. one)
    else
      M.show_long(title, output)
    end
  else
    local why = clean.line(err ~= nil and err ~= '' and err or 'failed')
    if output ~= '' then M.show_long(title .. ' (failed: ' .. why .. ')', output) end
    notify(title .. ' failed: ' .. why, levels.WARN)
  end
end

local function has_command(client, command)
  local caps = client.server_capabilities or {}
  local list = caps.executeCommandProvider and caps.executeCommandProvider.commands or {}
  return vim.tbl_contains(list, command)
end

--- capabilities.experimental.purrtty of a purrTTY server ({ subscribe, vehicleCommands, verb }), or nil.
local function purrtty_caps(client)
  local caps = client.server_capabilities or {}
  local p = type(caps.experimental) == 'table' and caps.experimental.purrtty
  return type(p) == 'table' and p or nil
end

--- False when the server has said it will refuse `command` (outside the game; kfs.verb switched
--- off): it is still sent -- the server's refusal is the one message -- but nothing is written first.
local function will_run(client, command)
  local p = purrtty_caps(client)
  if not p then return true end
  if p.vehicleCommands == false and command ~= 'kfs.manual' then return false end
  if command == 'kfs.verb' and p.verb == false then return false end
  return true
end

-- JSON-RPC errors (not refusals: those are ok = false results) in words. Neovim drops a
-- RequestCancelled (-32800) answer before it reaches a callback, and these commands are never
-- cancelled by the client, so that case is only a fallback.
local function rpc_error(command, err)
  local code = type(err) == 'table' and err.code or nil
  local msg = type(err) == 'table' and tostring(err.message or code) or tostring(err)
  if code == -32602 then return ('the language server does not know %s (%s)'):format(command, msg) end
  if code == -32601 then return 'this language server runs no commands' end
  if code == -32800 then return 'cancelled' end
  return msg
end

--- After a load or start that ran, follow the program's state, so a trap shows in the buffer.
local function follow_state(command, ok)
  if ok and (command == 'kfs.load' or command == 'kfs.start') then
    require('purrtty_kfs.log').want_state('program')
  end
end

--- Sends one contract command. `title` is what the player typed (`:KfsBuild`).
local function run(title, command, arguments, opts)
  opts = opts or {}
  local buf = api.nvim_get_current_buf()
  local client, r, why = kfs().client_for(buf)
  if not client then
    notify(title .. ': ' .. why, levels.WARN)
    return done(command, false, '', why)
  end
  if not client.initialized then
    notify(title .. ': still connecting to the language server', levels.WARN)
    return done(command, false, '', 'connecting')
  end
  if not has_command(client, command) then
    local msg = purrtty_caps(client)
      and ('this language server does not offer %s'):format(command)
      or ('needs the game: this language server (%s) offers diagnostics, hover and completion only'):format(
        r.candidate and r.candidate.kind == 'stdio' and 'purr-kfs lsp' or 'not the in-game one')
    notify(title .. ': ' .. msg, levels.WARN)
    return done(command, false, '', msg)
  end
  if opts.write and will_run(client, command) then
    local ok, err = write_workspace(r)
    if not ok then
      notify(title .. ': ' .. err, levels.ERROR)
      return done(command, false, '', err)
    end
  end
  if type(arguments) == 'function' then arguments = arguments(r) end
  if arguments == nil then return end
  client:request('workspace/executeCommand', { command = command, arguments = arguments }, function(err, result)
    if err then
      local msg = rpc_error(command, err)
      show(title, false, '', msg)
      return done(command, false, '', msg)
    end
    if type(result) ~= 'table' then
      show(title, false, '', 'the server sent no result')
      return done(command, false, '', 'no result')
    end
    local ok = result.ok == true
    local output = type(result.output) == 'string' and result.output or ''
    local e = type(result.error) == 'string' and result.error or nil
    show(title, ok, output, e)
    follow_state(command, ok)
    done(command, ok, output, e)
  end, buf)
end

local is_win = package.config:sub(1, 1) == '\\'

-- The file a command argument names, literally: never globbed (expand() would turn t[1].c into
-- t1.c), '~' and $VARS expanded, the backslash escapes of file completion removed (Unix); %, #
-- and <cfile>-style specials still go through expand().
local function argument_path(arg)
  if arg:match('^[%%#<]') then return vim.fn.expand(arg) end
  if not is_win then arg = arg:gsub('\\(.)', '%1') end
  return vim.fs.normalize(arg)
end

--- The URI of the file a :KfsBuild/:KfsLoad acts on: the argument, else the current buffer.
local function file_argument(title, command, arg)
  return function(r)
    local path
    if arg and arg ~= '' then
      path = vim.fs.abspath(argument_path(arg))
    else
      local b = api.nvim_get_current_buf()
      if vim.bo[b].buftype == '' then path = api.nvim_buf_get_name(b) end
    end
    local root = path and path ~= '' and kfs().root_of(path)
    if not root or root.keys[1] ~= r.keys[1] then
      notify(title .. ': run it in a kFS source buffer or name a file of the workspace ' .. r.path, levels.WARN)
      done(command, false, '', 'no file')
      return nil
    end
    -- The buffer of exactly this file (its own URI, as the server knows it), else the path's URI.
    -- Not vim.fn.bufnr(path): that matches patterns and partial names ('t[1].c' finds t1.c,
    -- a.c finds a loaded a.c.orig).
    local b = kfs()._buffer_named(path)
    return { b and vim.uri_from_bufnr(b) or vim.uri_from_fname(path) }
  end
end

function M.build(a) run(':KfsBuild', 'kfs.build', file_argument(':KfsBuild', 'kfs.build', a.args), { write = true }) end
function M.load(a) run(':KfsLoad', 'kfs.load', file_argument(':KfsLoad', 'kfs.load', a.args), { write = true }) end
function M.start() run(':KfsStart', 'kfs.start', {}) end
function M.stop() run(':KfsStop', 'kfs.stop', {}) end
function M.reset() run(':KfsReset', 'kfs.reset', {}) end

function M.target(a)
  local t = vim.trim(a.args or '')
  if t ~= 'vehicle' and t ~= 'bench' then
    notify(':KfsTarget takes vehicle or bench', levels.WARN)
    return done('kfs.target', false, '', 'bad argument')
  end
  run(':KfsTarget', 'kfs.target', { t })
end

function M.parts(a)
  local kind = vim.trim(a.args or '')
  if kind ~= '' and kind ~= 'lights' and kind ~= 'deploy' and kind ~= 'actuators' then
    notify(':KfsParts takes lights, deploy or actuators (or nothing for all)', levels.WARN)
    return done('kfs.parts', false, '', 'bad argument')
  end
  run(':KfsParts', 'kfs.parts', kind == '' and {} or { kind })
end

--- The code of the diagnostic under the cursor (else the first one with a code on that line).
local function code_under_cursor()
  local win = api.nvim_get_current_win()
  local buf = api.nvim_win_get_buf(win)
  local pos = api.nvim_win_get_cursor(win)
  local row, col = pos[1] - 1, pos[2]
  local first
  for _, d in ipairs(vim.diagnostic.get(buf, { lnum = row })) do
    if d.code ~= nil and d.code ~= vim.NIL then
      local code = tostring(d.code)
      local end_col = d.end_col or (d.col + 1)
      local multi = d.end_lnum and d.end_lnum > row
      if col >= d.col and (col < end_col or multi) then return code end
      first = first or code
    end
  end
  return first
end

function M.manual(a)
  local code = vim.trim(a.args or '')
  if code == '' then code = code_under_cursor() or '' end
  if code == '' then
    notify(':KfsManual: no diagnostic with a code under the cursor; give one, e.g. :KfsManual KC-E003', levels.WARN)
    return done('kfs.manual', false, '', 'no code')
  end
  run(':KfsManual', 'kfs.manual', { code })
end

function M.verb(a)
  run(':KfsVerb', 'kfs.verb', { a.args }, { write = true })
end

function M.log()
  require('purrtty_kfs.log').open()
end

function M.info()
  M.show_long(':KfsInfo', table.concat(kfs().info_lines(), '\n'))
end

function M.restart()
  kfs().restart()
  notify('looking for the language server again')
end

return M
