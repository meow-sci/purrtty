-- purrTTY kFS: the :KfsLog buffer, the purrtty/* feeds and the status string.
--
-- The in-game server sends, to clients that subscribed (`purrtty/subscribe`):
--   purrtty/kfsLog    { lines = { ... }, tail = true|nil } program log lines (tail: the log's last
--                                                   lines, sent once when `log` is subscribed)
--   purrtty/kfsOutput { text = '...' }              kfs command output
--   purrtty/kfsState  { program, state, tick, overruns, trap = nil | { message, uri, line } }
-- and, to `state` clients only, a trap as a KFS-TRAP diagnostic in the buffer.
-- `log` and `output` are subscribed while the log buffer exists; `state` while the log buffer
-- exists, once status() has been called (a statusline), and after the first :KfsLoad/:KfsStart
-- that ran. Not at attach: a `state` subscriber keeps the vehicle's computer publishing a
-- snapshot every tick, which only a program started from here (or a status display) is worth.
-- Every server string is cleaned before it reaches a buffer, a message or the status line
-- (purrtty_kfs.text: one line where one is expected, no escape sequences or control characters).

local M = {}

local api = vim.api
local text = require('purrtty_kfs.text')
local LOG_NAME = 'kfs://log'
local OUTPUT_PREFIX = '> '

local log_buf = nil
local want_state = {} -- reason ('status', 'program') -> true
local sent = {} -- client id -> the topic list last sent (as a string)

local function kfs()
  return require('purrtty_kfs')
end

-- The log buffer exists and is loaded: an unloaded one (:bdelete) would be loaded again as an
-- ordinary buffer by the next nvim_buf_set_lines, without its options or its history.
local function live(b)
  return b ~= nil and api.nvim_buf_is_valid(b) and api.nvim_buf_is_loaded(b)
end

local function topics()
  local t = {}
  local open = live(log_buf)
  if open then
    t[#t + 1] = 'log'
    t[#t + 1] = 'output'
  end
  if open or next(want_state) ~= nil then t[#t + 1] = 'state' end
  return t
end

local function supports_subscribe(client)
  local caps = client.server_capabilities or {}
  local p = caps.experimental and caps.experimental.purrtty
  return type(p) == 'table' and p.subscribe == true
end

--- Sends the wanted topic set to `client` when it differs from what it has.
local function subscribe(client)
  if not client.initialized or not supports_subscribe(client) then return end
  local t = topics()
  local s = table.concat(t, ',')
  if sent[client.id] == s then return end
  sent[client.id] = s
  client:notify('purrtty/subscribe', { topics = t })
end

local function resubscribe_all()
  for _, e in ipairs(kfs().clients()) do subscribe(e.client) end
end

--- Subscribes the kfs clients to the program state from now on (`reason`: 'status', 'program').
function M.want_state(reason)
  if want_state[reason] then return end
  want_state[reason] = true
  resubscribe_all()
end

--- The topics sent to the servers now (tests, :KfsInfo).
function M.topics()
  return topics()
end

--- Called when a kfs client has initialized (also after a reconnect).
function M.client_ready(r, client)
  sent[client.id] = nil
  r.last_state = nil
  r.last_trap = nil
  if #topics() > 0 then subscribe(client) end
end

-- ------------------------------------------------------------------ the buffer

--- Appends lines to the log buffer (if open), keeping at most log_max_lines; every window that
--- showed the last line keeps following the tail, the others stay where they are.
function M.append(lines)
  local b = log_buf
  if not live(b) or #lines == 0 then return end
  local last = api.nvim_buf_line_count(b)
  local empty = last == 1 and api.nvim_buf_get_lines(b, 0, 1, false)[1] == ''
  local follow = {}
  for _, w in ipairs(vim.fn.win_findbuf(b)) do
    follow[w] = api.nvim_win_get_cursor(w)[1] >= last
  end
  vim.bo[b].modifiable = true
  api.nvim_buf_set_lines(b, empty and 0 or -1, -1, false, lines)
  local count = api.nvim_buf_line_count(b)
  local cap = math.max(1, tonumber(kfs().config().log_max_lines) or 10000)
  if count > cap then
    api.nvim_buf_set_lines(b, 0, count - cap, false, {})
    count = cap
  end
  vim.bo[b].modifiable = false
  vim.bo[b].modified = false
  for w, f in pairs(follow) do
    if f and api.nvim_win_is_valid(w) then api.nvim_win_set_cursor(w, { count, 0 }) end
  end
end

local function forget(buf)
  if log_buf ~= buf then return end
  log_buf = nil
  vim.schedule(function()
    -- Deleted or unloaded rather than wiped: wipe it, so the name is free for the next :KfsLog.
    if api.nvim_buf_is_valid(buf) then pcall(api.nvim_buf_delete, buf, { force = true }) end
    resubscribe_all()
  end)
end

local function set_options(b)
  local bo = vim.bo[b]
  bo.buftype = 'nofile'
  bo.bufhidden = 'hide'
  bo.swapfile = false
  bo.modifiable = false
  if bo.filetype ~= 'kfslog' then bo.filetype = 'kfslog' end
end

--- :KfsLog — opens (or shows) the log buffer and subscribes the kfs clients to the feeds.
function M.open()
  if not live(log_buf) then
    if log_buf and api.nvim_buf_is_valid(log_buf) then pcall(api.nvim_buf_delete, log_buf, { force = true }) end
    -- A leftover buffer of that name (an old log deleted by hand) would make the name taken.
    for _, b in ipairs(api.nvim_list_bufs()) do
      if api.nvim_buf_get_name(b) == LOG_NAME then pcall(api.nvim_buf_delete, b, { force = true }) end
    end
    log_buf = api.nvim_create_buf(true, true)
    api.nvim_buf_set_name(log_buf, LOG_NAME)
    -- :bdelete and :bunload count like :bwipeout: the log is gone and unsubscribed.
    api.nvim_create_autocmd({ 'BufWipeout', 'BufDelete', 'BufUnload' }, {
      buffer = log_buf,
      desc = 'purrTTY kFS: unsubscribe the log',
      callback = function(ev) forget(ev.buf) end,
    })
  end
  set_options(log_buf)
  local wins = vim.fn.win_findbuf(log_buf)
  if #wins == 0 then
    vim.cmd('botright split')
    api.nvim_win_set_buf(0, log_buf)
    api.nvim_win_set_height(0, math.max(5, math.floor(vim.o.lines / 3)))
  else
    api.nvim_set_current_win(wins[1])
  end
  local clients = kfs().clients()
  local any = false
  for _, e in ipairs(clients) do
    if supports_subscribe(e.client) then any = true end
    subscribe(e.client)
  end
  if #clients == 0 then
    kfs().notify('not connected yet; the log fills once a language server is attached', vim.log.levels.INFO)
  elseif not any then
    kfs().notify('this language server sends no program log (that needs the game: purr nvim in KSA)', vim.log.levels.INFO)
  end
  return log_buf
end

--- The log buffer, or nil.
function M.buffer()
  return live(log_buf) and log_buf or nil
end

-- ------------------------------------------------------------------ state and status

local function is_nil(x)
  return x == nil or x == vim.NIL
end

-- 'trap DIV_ZERO in task 0, function step (main.c:12)': the server's message (the host's text,
-- which starts with "trap"; "trap " is added when it does not) and where, on one line.
local function trap_text(trap)
  local where = ''
  if type(trap.uri) == 'string' then
    local ok, fname = pcall(vim.uri_to_fname, trap.uri)
    where = vim.fs.basename(ok and fname or trap.uri)
    if type(trap.line) == 'number' then where = where .. ':' .. (trap.line + 1) end
    where = ' (' .. text.line(where) .. ')'
  end
  local message = text.line(is_nil(trap.message) and 'trap' or trap.message)
  if not message:lower():match('^trap') then message = 'trap ' .. message end
  return message .. where
end

--- One line describing root record r's last program state, or nil.
function M.state_line(r)
  local s = r and r.last_state
  if not s then return nil end
  local parts = { text.line(is_nil(s.state) and '?' or s.state) }
  if not is_nil(s.program) then parts[#parts + 1] = text.line(s.program) end
  if type(s.tick) == 'number' then parts[#parts + 1] = 'tick ' .. s.tick end
  if type(s.overruns) == 'number' and s.overruns > 0 then parts[#parts + 1] = s.overruns .. ' overruns' end
  local line = table.concat(parts, ' ')
  if not is_nil(s.trap) and type(s.trap) == 'table' then line = line .. ', ' .. trap_text(s.trap) end
  return line
end

local function on_state(r, params)
  if type(params) ~= 'table' then return end
  r.last_state = params
  local trap = params.trap
  if is_nil(trap) or type(trap) ~= 'table' then
    r.last_trap = nil
  else
    local k = table.concat({ tostring(trap.message), tostring(trap.uri), tostring(trap.line) }, '|')
    if r.last_trap ~= k then
      r.last_trap = k
      local t = trap_text(trap)
      kfs().notify(t, vim.log.levels.ERROR)
      M.append({ '!! ' .. t })
    end
  end
  pcall(vim.cmd, 'redrawstatus')
end

--- The handlers of one kfs client (root record r).
function M.handlers(r)
  return {
    ['purrtty/kfsLog'] = function(_, params)
      local out = {}
      for _, l in ipairs(type(params) == 'table' and type(params.lines) == 'table' and params.lines or {}) do
        for _, x in ipairs(text.lines(l)) do out[#out + 1] = x end
        if l == '' then out[#out + 1] = '' end
      end
      if #out > 0 and type(params) == 'table' and params.tail == true then
        table.insert(out, 1, ('-- the program log before this (its last %d lines) --'):format(#out))
      end
      M.append(out)
    end,
    ['purrtty/kfsOutput'] = function(_, params)
      local out = {}
      for _, x in ipairs(text.lines(type(params) == 'table' and params.text or nil)) do out[#out + 1] = OUTPUT_PREFIX .. x end
      M.append(out)
    end,
    ['purrtty/kfsState'] = function(_, params)
      on_state(r, params)
    end,
  }
end

--- The statusline string (see require('purrtty_kfs').status()).
function M.status()
  M.want_state('status')
  local k = kfs()
  local client, r = k.client_for(api.nvim_get_current_buf())
  if not client then
    local all = k.clients()
    if #all == 0 then return '' end
    client, r = all[1].client, all[1].root
  end
  if not client.initialized then return 'kFS: connecting' end
  subscribe(client)
  local line = M.state_line(r)
  return line and ('kFS: ' .. line) or 'kFS: connected'
end

return M
