-- purrTTY kFS for Neovim: filetypes, connection discovery and attach for kC and kASM.
--
-- Loaded by the entry script ../../purrtty_kfs.lua (which the purrTTY mod runs through
-- `--cmd "lua dofile(...)"`) or by ../../plugin/purrtty_kfs.lua when this folder is installed as
-- an ordinary plugin. Nothing here depends on 'runtimepath', and setup() is idempotent.
-- See ../../doc/purrtty-kfs.txt.

local M = {}

M.version = '0.1.0'

--- The oldest Neovim these files are written for (vim.uv, vim.lsp.start/get_clients,
--- vim.lsp.rpc.connect, client:request method calls, vim.fs.abspath).
M.min_nvim = '0.11'

local api = vim.api
local uv = vim.uv or vim.loop

local is_win = package.config:sub(1, 1) == '\\'

local NAME = 'kfs' -- the LSP client name
local GROUP = 'purrtty_kfs'

-- Where these files are: <dir>/lua/purrtty_kfs/init.lua -> <dir>.
local function runtime_dir()
  local source = debug.getinfo(1, 'S').source
  if source:sub(1, 1) == '@' then source = source:sub(2) end
  source = source:gsub('\\', '/')
  return source:match('^(.*)/lua/purrtty_kfs/[^/]*$') or source:match('^(.*)/[^/]*$') or '.'
end

local defaults = {
  -- Extra kFS workspace folder(s) (a string or a list), for a workspace in a non-default place
  -- when Neovim is started outside the game. PURRTTY_KFS_ROOT and the server info file add theirs.
  root = nil,
  -- How often a buffer without a server looks for one again (milliseconds).
  retry_interval_ms = 3000,
  -- How long a TCP probe of a server address may take (milliseconds; loopback answers at once).
  probe_timeout_ms = 500,
  -- Fall back to `purr-kfs lsp --stdio` when it is on PATH.
  stdio = true,
  -- The purr-kfs executable (a name looked up on PATH, or a path).
  purr_kfs = 'purr-kfs',
  -- Lines kept in the :KfsLog buffer.
  log_max_lines = 10000,
  -- Start tree-sitter highlighting (the C parser) in kC buffers.
  treesitter = false,
  -- Set the buffer options of kc/kasm buffers ('commentstring', 'cindent', ...). false: leave them
  -- to your own FileType handlers or ftplugin (these run after them otherwise and would win).
  buffer_options = true,
  -- How long a new connection may take to answer `initialize` before it is given up and retried
  -- (milliseconds).
  initialize_timeout_ms = 5000,
  -- A server that closes the connection (or never answers) before `initialize` this many times
  -- in a row is not tried again until its address or token changes, or :KfsRestart.
  max_attempts = 3,
}

-- PURRTTY_KFS_TOKEN, read once at setup and then removed from Neovim's environment, so neither
-- :terminal, :! nor any plugin's child process inherits it. Not in M._state (exposed for tests).
-- A server restarted later is found through its info file, which carries its own token.
local env_token = nil

local state = {
  setup_calls = 0, -- every setup() call (both entries may run)
  setup_done = false, -- the one time it did something
  too_old = false,
  opts = {},
  dir = runtime_dir(),
  roots = {}, -- root key -> root record
  once = {}, -- message keys already shown
  exiting = false,
  cache = nil, -- { at = hrtime, roots = {...}, infos = {...} }
  refusal_shown = false,
}
M._state = state

--- The effective configuration: defaults < setup(opts) < vim.g.purrtty_kfs.
function M.config()
  local g = vim.g.purrtty_kfs
  return vim.tbl_deep_extend('force', defaults, state.opts, type(g) == 'table' and g or {})
end

-- ------------------------------------------------------------------ messages

local levels = vim.log.levels

--- Shows `kFS: <msg>`; with `once`, only the first time that key is used in this session.
function M.notify(msg, level, once)
  if once then
    if state.once[once] then return end
    state.once[once] = true
  end
  local function show()
    vim.notify('kFS: ' .. msg, level or levels.INFO, { title = 'purrtty_kfs' })
  end
  if vim.in_fast_event() then vim.schedule(show) else show() end
end

-- ------------------------------------------------------------------ paths

local function nonempty(s)
  return type(s) == 'string' and s ~= '' and s or nil
end

-- Forward slashes, no duplicate or trailing separator, upper-case drive letter. Windows paths
-- arrive as C:\Users\... (the environment, the info file) or C:/Users/... (Neovim's buffer names
-- on Windows use backslashes too); both become C:/Users/.... A UNC prefix (\\server) keeps its
-- two slashes.
local function norm(p)
  if not nonempty(p) then return nil end
  p = p:gsub('\\', '/')
  local unc = p:sub(1, 2) == '//'
  p = p:gsub('/+', '/')
  if unc then p = '/' .. p end
  if #p > 1 and p:sub(-1) == '/' and not p:match('^%a:/$') then p = p:sub(1, -2) end
  p = p:gsub('^(%a):', function(d) return d:upper() .. ':' end)
  return p
end
M._norm = norm

-- A configured or environment path made absolute: '~' and environment variables expanded,
-- relative to Neovim's working directory (the workspace root when the mod launched it).
local function absolute(p)
  if not nonempty(p) then return nil end
  local ok, n = pcall(vim.fs.normalize, p)
  if not ok or not nonempty(n) then return nil end
  local ok2, a = pcall(vim.fs.abspath, n)
  return norm(ok2 and a or n)
end
M._absolute = absolute

-- $PURRTTY_KFS_ROOT, absolute.
local function env_root()
  return absolute(vim.env.PURRTTY_KFS_ROOT)
end

-- The comparison key: Windows file names are case-insensitive (drive letter and the rest);
-- elsewhere paths compare exactly as written.
local function key(p)
  p = norm(p)
  if p and is_win then p = p:lower() end
  return p
end

local function realpath(p)
  local ok, r = pcall(uv.fs_realpath, p)
  return ok and r or nil
end

-- Both keys of a path: as written, and with symbolic links resolved (when it exists).
local function keys_of(p)
  local k = key(p)
  if not k then return {} end
  local list = { k }
  local real = realpath(p)
  local rk = real and key(real)
  if rk and rk ~= k then list[#list + 1] = rk end
  return list
end

local function is_under(path_key, root_key)
  if path_key == root_key then return true end
  local prefix = root_key:sub(-1) == '/' and root_key or root_key .. '/'
  return path_key:sub(1, #prefix) == prefix
end

local function dirname(p)
  p = norm(p)
  if not p then return nil end
  return p:match('^(.*)/[^/]*$')
end

local function is_dir(p)
  local st = p and uv.fs_stat(p)
  return st and st.type == 'directory' or false
end

local function join(a, b)
  return (a:sub(-1) == '/' or a:sub(-1) == '\\') and a .. b or a .. '/' .. b
end

-- ------------------------------------------------------------------ the default locations

--- The folders .NET calls "My Documents" on this OS (the game's DocumentsFolderPath parent).
local function documents_dirs()
  local out = {}
  if is_win then
    for _, base in ipairs({ vim.env.USERPROFILE, vim.env.OneDrive }) do
      if nonempty(base) then out[#out + 1] = join(base, 'Documents') end
    end
  else
    local home = nonempty(vim.env.HOME) or uv.os_homedir()
    local mac = (uv.os_uname().sysname or '') == 'Darwin'
    if not mac and nonempty(vim.env.XDG_DOCUMENTS_DIR) then out[#out + 1] = vim.env.XDG_DOCUMENTS_DIR end
    if home then
      out[#out + 1] = join(home, 'Documents')
      -- .NET before 8 returned $HOME itself for "My Documents" on Linux.
      if not mac then out[#out + 1] = home end
    end
  end
  return out
end

--- <Documents>/My Games/Kitten Space Agency for every Documents candidate.
local function game_dirs()
  local out = {}
  for _, d in ipairs(documents_dirs()) do out[#out + 1] = join(join(d, 'My Games'), 'Kitten Space Agency') end
  return out
end

local function config_roots(cfg)
  local list = {}
  local r = cfg.root
  if type(r) == 'string' then r = { r } end
  if type(r) == 'table' then
    for _, x in ipairs(r) do
      local a = absolute(x)
      if a then list[#list + 1] = a end
    end
  end
  return list
end

--- The folders that may hold `.purrTTY/kfs-lsp.json`: $PURRTTY_CONFIG_DIR alone when it is set;
--- otherwise the parent of every known workspace root (the default layout puts `.purrTTY/` beside
--- `kfs/`) and <Documents>/My Games/Kitten Space Agency.
local function info_parents(cfg)
  local cd = absolute(vim.env.PURRTTY_CONFIG_DIR)
  if cd then return { cd } end
  local out, seen = {}, {}
  local function add(p)
    local k = key(p)
    if k and not seen[k] then
      seen[k] = true
      out[#out + 1] = norm(p)
    end
  end
  local er = env_root()
  if er then add(dirname(er)) end
  for _, r in ipairs(config_roots(cfg)) do add(dirname(r)) end
  for _, g in ipairs(game_dirs()) do add(g) end
  return out
end

local function parse_address(a)
  if type(a) ~= 'string' then return nil end
  local host, port = a:match('^%[(.+)%]:(%d+)$')
  if not host then host, port = a:match('^([^:]+):(%d+)$') end
  port = tonumber(port)
  if not host or not port or port < 1 or port > 65535 then return nil end
  if host == 'localhost' then host = '127.0.0.1' end
  return host, port
end

--- Reads one server info file. Nil when there is none; a record with `error` when it is unusable.
local function read_info(parent)
  local path = join(join(parent, '.purrTTY'), 'kfs-lsp.json')
  local fd = uv.fs_open(path, 'r', 438)
  if not fd then return nil end
  local st = uv.fs_fstat(fd)
  local data = st and st.size > 0 and st.size < 1048576 and uv.fs_read(fd, st.size, 0) or nil
  uv.fs_close(fd)
  local info = { path = path }
  if not data then
    info.error = 'empty or unreadable'
    return info
  end
  if data:sub(1, 3) == '\239\187\191' then data = data:sub(4) end -- a UTF-8 byte order mark
  local ok, obj = pcall(vim.json.decode, data, { luanil = { object = true, array = true } })
  if not ok or type(obj) ~= 'table' then
    info.error = 'not a JSON object'
    return info
  end
  info.address = type(obj.address) == 'string' and obj.address or nil
  info.token = type(obj.token) == 'string' and obj.token or nil
  info.pid = type(obj.pid) == 'number' and obj.pid or nil
  info.ksa_build = type(obj.ksa_build) == 'string' and obj.ksa_build or nil
  info.workspace_root = type(obj.workspace_root) == 'string' and obj.workspace_root or nil
  info.host, info.port = parse_address(info.address)
  if not info.host then
    info.error = 'no usable "address"'
  elseif not info.workspace_root then
    info.error = 'no "workspace_root"'
  end
  return info
end

-- ------------------------------------------------------------------ workspace roots

--- Every known workspace root, recomputed at most once a second (or when `fresh`):
--- { path = <normalized>, keys = {...}, source = <how it is known> }, plus the info files read.
local function known(fresh)
  local now = uv.hrtime()
  local c = state.cache
  if c and not fresh and now - c.at < 1e9 then return c end
  local cfg = M.config()
  local roots, seen, infos = {}, {}, {}
  local function add(p, source)
    local n = norm(p)
    if not n then return end
    local ks = keys_of(n)
    for _, k in ipairs(ks) do
      if seen[k] then return end
    end
    for _, k in ipairs(ks) do seen[k] = true end
    roots[#roots + 1] = { path = n, keys = ks, source = source }
  end
  add(env_root(), 'PURRTTY_KFS_ROOT')
  for _, r in ipairs(config_roots(cfg)) do add(r, 'configuration (root)') end
  for _, parent in ipairs(info_parents(cfg)) do
    local info = read_info(parent)
    if info then
      infos[#infos + 1] = info
      if not info.error then add(info.workspace_root, 'server info file ' .. info.path) end
    end
  end
  for _, g in ipairs(game_dirs()) do
    local d = join(g, 'kfs')
    if is_dir(d) then add(d, 'default workspace location') end
  end
  state.cache = { at = now, roots = roots, infos = infos, parents = info_parents(cfg) }
  return state.cache
end

--- The loaded file buffer named exactly `path` (symbolic links and, on Windows, letter case
--- aside), or nil. Never vim.fn.bufnr(path), which matches patterns and partial names.
function M._buffer_named(path)
  local want = keys_of(path)
  for _, b in ipairs(api.nvim_list_bufs()) do
    if api.nvim_buf_is_loaded(b) and vim.bo[b].buftype == '' then
      local name = api.nvim_buf_get_name(b)
      if name ~= '' then
        for _, k in ipairs(keys_of(name)) do
          for _, w in ipairs(want) do
            if k == w then return b end
          end
        end
      end
    end
  end
  return nil
end

--- The workspace root that holds `path` (the deepest one), or nil.
function M.root_of(path)
  local pks = keys_of(path)
  if #pks == 1 then
    -- The file may not exist yet: resolve its folder instead.
    local d = dirname(path)
    local real = d and realpath(d)
    local base = norm(path):match('[^/]*$')
    if real and base then
      local rk = key(join(real, base))
      if rk ~= pks[1] then pks[2] = rk end
    end
  end
  local best, best_len
  for _, root in ipairs(known().roots) do
    for _, rk in ipairs(root.keys) do
      for _, pk in ipairs(pks) do
        if is_under(pk, rk) and (not best_len or #rk > best_len) then
          best, best_len = root, #rk
        end
      end
    end
  end
  return best
end

local function same_root(path, r)
  local ks = keys_of(path)
  for _, k in ipairs(ks) do
    for _, rk in ipairs(r.keys) do
      if k == rk then return true end
    end
  end
  return false
end

-- The record of one workspace root: its client, how it was found, discovery state.
local function root_state(root)
  local k = root.keys[1]
  local r = state.roots[k]
  if not r then
    r = {
      path = root.path,
      keys = root.keys,
      source = root.source,
      client_id = nil,
      candidate = nil, -- how the current client was found
      tried = {}, -- the last discovery: { how, result }
      refused = {}, -- candidate id -> reason (wrong token, initialize refused)
      failed = {}, -- candidate id -> reason (a stdio server that exited with an error, a server that
      --              closed before `initialize` max_attempts times)
      attempts = {}, -- candidate id -> connections in a row that ended before `initialize`
      backoff = 1, -- the retry interval's multiplier (doubles after each attempt that ended early)
      runs = 0, -- discovery runs
      discovering = false,
      timer = nil,
      lost = false, -- the server went away; announce the reconnect
    }
    state.roots[k] = r
  end
  return r
end

local function is_kfs_buf(b)
  if not api.nvim_buf_is_loaded(b) or vim.bo[b].buftype ~= '' then return false end
  local ft = vim.bo[b].filetype
  return ft == 'kc' or ft == 'kasm'
end

--- The loaded kc/kasm buffers below root record `r`.
local function buffers_of(r)
  local out = {}
  for _, b in ipairs(api.nvim_list_bufs()) do
    if is_kfs_buf(b) then
      local name = api.nvim_buf_get_name(b)
      if name ~= '' then
        local root = M.root_of(name)
        if root and root.keys[1] == r.keys[1] then out[#out + 1] = b end
      end
    end
  end
  return out
end
M._buffers_of = buffers_of

--- The live kfs client of root record `r`, or nil.
local function client_of(r)
  local c = r.client_id and vim.lsp.get_client_by_id(r.client_id)
  if c and not c:is_stopped() then return c end
  return nil
end

-- ------------------------------------------------------------------ discovery

local function alive(pid)
  if type(pid) ~= 'number' or pid <= 0 then return true end -- unknown: the probe decides
  local ok, _, name = uv.kill(pid, 0)
  if ok == 0 then return true end
  return name == 'EPERM' -- exists, owned by someone else
end

--- Connects to host:port and closes again; `cb(ok, err)` runs on the main loop.
local function probe(host, port, timeout, cb)
  local tcp = uv.new_tcp()
  local timer = uv.new_timer()
  local done = false
  local function finish(ok, err)
    if done then return end
    done = true
    timer:stop()
    if not timer:is_closing() then timer:close() end
    if not tcp:is_closing() then tcp:close() end
    vim.schedule(function() cb(ok, err) end)
  end
  timer:start(timeout, 0, function() finish(false, 'no answer within ' .. timeout .. ' ms') end)
  local ok, err = pcall(tcp.connect, tcp, host, port, function(e) finish(e == nil, e) end)
  if not ok or err == nil then finish(false, tostring(err)) end
end

--- The connection candidates for root record `r`, in order (plan §7.6).
local function candidates(r, cfg)
  local list = {}
  local env = nonempty(vim.env.PURRTTY_KFS_LSP)
  if env then
    local er = env_root()
    if not er or same_root(er, r) then
      local host, port = parse_address(env)
      list[#list + 1] = {
        kind = 'environment', how = 'environment (PURRTTY_KFS_LSP=' .. env .. ')',
        address = env, host = host, port = port, token = env_token,
        bad = not host and ('PURRTTY_KFS_LSP is not <host>:<port>: ' .. env) or nil,
      }
    end
  end
  for _, info in ipairs(known(true).infos) do
    if not info.error and same_root(info.workspace_root, r) then
      list[#list + 1] = {
        kind = 'info', how = 'server info file ' .. info.path,
        address = info.address, host = info.host, port = info.port, token = info.token, pid = info.pid,
        ksa_build = info.ksa_build,
      }
      -- The live server's own file names the environment's address with another token: the server
      -- was restarted on a fixed lsp_port (`purr lsp restart`), so the launch-time token is stale.
      -- Skip it rather than be refused (and say "refused this session") first.
      local e = list[1]
      if e and e.kind == 'environment' and not e.bad and alive(info.pid) and e.host == info.host and e.port == info.port
          and e.token ~= info.token then
        e.bad = 'superseded by ' .. info.path .. ' (same address, a newer token: the server was restarted)'
      end
    end
  end
  if cfg.stdio ~= false and nonempty(cfg.purr_kfs) then
    local exe = vim.fn.exepath(cfg.purr_kfs)
    if exe ~= '' then
      local cmd = { exe, 'lsp', '--stdio', '--workspace', r.path }
      if is_win and exe:lower():match('%.[cb][ma][dt]$') then
        cmd = { nonempty(vim.env.ComSpec) or 'cmd.exe', '/c', exe, 'lsp', '--stdio', '--workspace', r.path }
      end
      list[#list + 1] = { kind = 'stdio', how = 'purr-kfs lsp --stdio (' .. exe .. ')', cmd = cmd }
    end
  end
  -- The id: what makes a candidate "the same" for refusals and failures — kind, address and a
  -- fingerprint of the token (a new token is a new candidate), never the token itself.
  for _, c in ipairs(list) do
    local fp = c.token and c.token ~= '' and vim.fn.sha256(c.token):sub(1, 12) or ''
    c.id = c.kind .. '|' .. (c.address or table.concat(c.cmd or {}, ' ')) .. '|' .. fp
  end
  return list
end

local start_client, schedule_retry, stop_retry

local function none_found(r)
  r.candidate = nil
  -- One message per session; none after a refusal or a lost connection (already said).
  if not state.refusal_shown and not r.lost then
    M.notify('no language server found; syntax only for now (:KfsInfo)', levels.INFO, 'none')
  end
  schedule_retry(r)
end

--- Looks for a server for root record `r` (asynchronously) and attaches its buffers when found.
local function discover(r)
  if r.discovering or state.exiting then return end
  if client_of(r) then return end
  r.discovering = true
  r.runs = r.runs + 1
  local cfg = M.config()
  local list = candidates(r, cfg)
  local tried = {}
  r.tried = tried
  local i = 0
  local function step()
    i = i + 1
    local c = list[i]
    if not c then
      r.discovering = false
      return none_found(r)
    end
    local function skip(why)
      tried[#tried + 1] = { how = c.how, result = why }
      return step()
    end
    if r.refused[c.id] then return skip('refused earlier: ' .. r.refused[c.id]) end
    if r.failed[c.id] then return skip('failed earlier: ' .. r.failed[c.id]) end
    if c.bad then return skip(c.bad) end
    if c.kind == 'stdio' then
      r.discovering = false
      tried[#tried + 1] = { how = c.how, result = 'started' }
      return start_client(r, c)
    end
    if c.pid and not alive(c.pid) then return skip('stale: process ' .. c.pid .. ' is not running') end
    probe(c.host, c.port, cfg.probe_timeout_ms, function(ok, err)
      if state.exiting then
        r.discovering = false
        return
      end
      if not ok then return skip('no server at ' .. c.address .. ' (' .. tostring(err) .. ')') end
      r.discovering = false
      tried[#tried + 1] = { how = c.how, result = 'connected' }
      start_client(r, c)
    end)
  end
  step()
end

-- A TCP `cmd` that intercepts a refused `initialize` (wrong token): Neovim would otherwise raise
-- an error from inside its RPC callback; this says it once and closes the connection instead.
local function tcp_cmd(r, c)
  return function(dispatchers, ...)
    local rpc = vim.lsp.rpc.connect(c.host, c.port)(dispatchers, ...)
    local request = rpc.request
    rpc.request = function(method, params, callback, notify_reply_callback)
      if method == 'initialize' and callback then
        local inner = callback
        callback = function(err, result)
          if err then
            local why = require('purrtty_kfs.text').line(type(err) == 'table' and (err.message or tostring(err.code)) or tostring(err))
            r.refused[c.id] = why
            r.refusing = true
            state.refusal_shown = true
            M.notify(('%s refused this session (%s); not retrying it (:KfsInfo, :KfsRestart)'):format(c.address, why),
              levels.WARN, 'refused|' .. c.id)
            rpc.terminate()
            return
          end
          return inner(err, result)
        end
      end
      return request(method, params, callback, notify_reply_callback)
    end
    return rpc
  end
end

-- A connection that ended (or was given up) before `initialize` was answered: counted against
-- its candidate, which is not tried again after max_attempts in a row; the retry interval
-- doubles each time (up to 16 times). Nothing is announced: nothing was ever connected.
local function ended_early(r, c, why)
  if c then
    local n = (r.attempts[c.id] or 0) + 1
    r.attempts[c.id] = n
    local max = math.max(1, tonumber(M.config().max_attempts) or 3)
    if n >= max then
      r.failed[c.id] = ('%s %d times in a row (until its address or token changes, or :KfsRestart)'):format(why, n)
    end
    r.tried[#r.tried + 1] = { how = c.how, result = why }
  end
  r.backoff = math.min(16, (r.backoff or 1) * 2)
  stop_retry(r) -- a new timer with the longer interval
  schedule_retry(r)
end

local function on_exit(r, id, code)
  if r.client_id ~= id then return end
  r.client_id = nil
  local c = r.candidate
  local initialized = r.initialized_id == id
  r.initialized_id = nil
  if r.refusing then
    r.refusing = false
    schedule_retry(r)
    return
  end
  if state.exiting then return end
  if c and c.kind == 'stdio' and code ~= 0 then
    r.failed[c.id] = 'exited with code ' .. tostring(code)
  end
  if not initialized then return ended_early(r, c, 'closed before answering initialize') end
  r.lost = true
  M.notify('the language server went away; buffers reattach when it is back', levels.INFO)
  schedule_retry(r)
end

-- Neovim calls neither on_init nor on_exit for a connection refused after the probe succeeded,
-- or a peer that accepts and never answers `initialize`: the client would stay "connecting"
-- for good. Not initialized within initialize_timeout_ms: stop it, forget it, try again later.
local function watch(r, c, id)
  local ms = math.max(100, tonumber(M.config().initialize_timeout_ms) or 5000)
  local t = uv.new_timer()
  t:start(ms, 0, vim.schedule_wrap(function()
    t:stop()
    if not t:is_closing() then t:close() end
    if r.client_id ~= id or r.initialized_id == id or state.exiting then return end
    r.client_id = nil -- its on_exit, if it ever comes, is then ignored
    local client = vim.lsp.get_client_by_id(id)
    if client then pcall(client.stop, client, true) end
    ended_early(r, c, ('no answer to initialize within %d ms'):format(ms))
  end))
end

start_client = function(r, c)
  local bufs = buffers_of(r)
  if #bufs == 0 then return end
  local log = require('purrtty_kfs.log')
  local config = {
    name = NAME,
    cmd = c.kind == 'stdio' and c.cmd or tcp_cmd(r, c),
    root_dir = r.path,
    workspace_folders = { { uri = vim.uri_from_fname(r.path), name = r.path } },
    init_options = c.token and { token = c.token } or nil,
    handlers = log.handlers(r),
    flags = { debounce_text_changes = 150 },
    on_init = function(client, result)
      vim.schedule(function()
        if r.client_id == client.id then
          r.initialized_id = client.id
          r.attempts[c.id] = nil
          r.backoff = 1
        end
        r.server_info = result and result.serverInfo
        if r.lost then
          r.lost = false
          M.notify('reattached to the language server (' .. c.kind .. ')', levels.INFO)
        end
        log.client_ready(r, client)
      end)
    end,
    on_exit = function(code, _, id)
      vim.schedule(function() on_exit(r, id, code) end)
    end,
  }
  local ok, id = pcall(vim.lsp.start, config, {
    bufnr = bufs[1],
    attach = false,
    silent = true,
    reuse_client = function() return false end,
  })
  if not ok or not id then
    local why = ok and 'could not start' or tostring(id)
    r.failed[c.id] = why
    r.tried[#r.tried + 1] = { how = c.how, result = why }
    return none_found(r)
  end
  r.client_id = id
  -- Kept for :KfsInfo and the commands; without the token (M._state is visible to every plugin).
  r.candidate = { kind = c.kind, how = c.how, address = c.address, id = c.id }
  for _, b in ipairs(bufs) do vim.lsp.buf_attach_client(b, id) end
  stop_retry(r)
  watch(r, r.candidate, id)
end

schedule_retry = function(r)
  if r.timer or state.exiting then return end
  local interval = math.max(50, tonumber(M.config().retry_interval_ms) or 3000) * (r.backoff or 1)
  local t = uv.new_timer()
  r.timer = t
  t:start(interval, interval, vim.schedule_wrap(function()
    if r.timer ~= t then return end
    if state.exiting or #buffers_of(r) == 0 or client_of(r) then return stop_retry(r) end
    discover(r)
  end))
end

stop_retry = function(r)
  local t = r.timer
  r.timer = nil
  if t then
    t:stop()
    if not t:is_closing() then t:close() end
  end
end

--- Attaches `buf` (a kc/kasm buffer) to its workspace's client, discovering one when needed.
function M.attach(buf)
  if not api.nvim_buf_is_valid(buf) or vim.bo[buf].buftype ~= '' then return end
  local name = api.nvim_buf_get_name(buf)
  if name == '' then return end
  local root = M.root_of(name)
  if not root then return end -- a .kasm file outside every workspace: syntax only
  local r = root_state(root)
  local c = client_of(r)
  if c then
    vim.lsp.buf_attach_client(buf, c.id)
    return
  end
  discover(r)
end

--- The kfs client for `buf` (its workspace's), else the only kfs client there is.
--- Returns client, root record, or nil and why not.
function M.client_for(buf)
  buf = buf or api.nvim_get_current_buf()
  local name = api.nvim_buf_get_name(buf)
  local root = name ~= '' and M.root_of(name)
  if root then
    local r = root_state(root)
    local c = client_of(r)
    if c then return c, r end
    if r.discovering then return nil, r, 'still looking for the language server' end
    return nil, r, 'not connected to a language server (:KfsInfo)'
  end
  local found, fr = {}, nil
  for _, r in pairs(state.roots) do
    local c = client_of(r)
    if c then
      found[#found + 1] = c
      fr = r
    end
  end
  if #found == 1 then return found[1], fr end
  if #found == 0 then return nil, nil, 'not connected to a language server (:KfsInfo)' end
  return nil, nil, 'several kFS workspaces are open; run this from a buffer of the one you mean'
end

--- Every live kfs client managed here, with its root record.
function M.clients()
  local out = {}
  for _, r in pairs(state.roots) do
    local c = client_of(r)
    if c then out[#out + 1] = { client = c, root = r } end
  end
  return out
end

--- :KfsRestart — stops every kfs client, forgets refusals and failures and discovers again.
function M.restart()
  state.cache = nil
  state.refusal_shown = false
  for k in pairs(state.once) do
    if k == 'none' or k:find('^refused|') then state.once[k] = nil end
  end
  for _, r in pairs(state.roots) do
    stop_retry(r)
    r.refused, r.failed, r.attempts, r.backoff = {}, {}, {}, 1
    local c = client_of(r)
    if c then
      r.client_id = nil -- its on_exit is then ignored: no "went away", no reconnect timer
      c:stop()
    end
  end
  -- Re-detect buffers whose workspace became known later, then attach everything.
  for _, b in ipairs(api.nvim_list_bufs()) do
    if api.nvim_buf_is_loaded(b) and vim.bo[b].buftype == '' and api.nvim_buf_get_name(b) ~= '' then
      local ft = vim.filetype.match({ buf = b })
      if (ft == 'kc' or ft == 'kasm') and vim.bo[b].filetype ~= ft then
        vim.bo[b].filetype = ft
      elseif ft == vim.bo[b].filetype and is_kfs_buf(b) then
        M.attach(b)
      end
    end
  end
end

--- A short status for a statusline: '' without a kfs client; otherwise the connection and the
--- program's state (subscribes to `purrtty/kfsState` the first time it is called).
function M.status()
  return require('purrtty_kfs.log').status()
end

--- The lines of :KfsInfo.
function M.info_lines()
  local cfg = M.config()
  local k = known(true)
  local v = vim.version()
  local lines = {
    ('purrTTY kFS for Neovim %s (Neovim %d.%d.%d; needs %s or newer)'):format(M.version, v.major, v.minor, v.patch, M.min_nvim),
    'runtime files: ' .. state.dir,
    '',
    'environment:',
  }
  for _, name in ipairs({ 'PURRTTY_KFS_ROOT', 'PURRTTY_KFS_LSP', 'PURRTTY_KFS_NVIM', 'PURRTTY_CONFIG_DIR' }) do
    lines[#lines + 1] = ('  %s = %s'):format(name, nonempty(vim.env[name]) or '(not set)')
  end
  lines[#lines + 1] = '  PURRTTY_KFS_TOKEN ' .. (nonempty(env_token) and 'was set (held by these files, removed from the environment)' or 'was not set')
  lines[#lines + 1] = ''
  lines[#lines + 1] = 'server info files (.purrTTY/kfs-lsp.json) looked for in:'
  for _, p in ipairs(k.parents) do
    local info
    for _, x in ipairs(k.infos) do
      if key(dirname(dirname(x.path))) == key(p) then info = x end
    end
    if not info then
      lines[#lines + 1] = '  ' .. join(join(p, '.purrTTY'), 'kfs-lsp.json') .. ': none'
    elseif info.error then
      lines[#lines + 1] = '  ' .. info.path .. ': unusable (' .. info.error .. ')'
    else
      local t = require('purrtty_kfs.text')
      lines[#lines + 1] = ('  %s: %s, pid %s (%s), workspace %s, KSA %s'):format(info.path, t.line(info.address),
        tostring(info.pid or '?'), alive(info.pid) and 'running' or 'not running', t.line(info.workspace_root), t.line(info.ksa_build or '?'))
    end
  end
  local exe = cfg.stdio ~= false and nonempty(cfg.purr_kfs) and vim.fn.exepath(cfg.purr_kfs) or ''
  lines[#lines + 1] = 'purr-kfs on PATH: ' .. (exe ~= '' and exe or (cfg.stdio == false and '(disabled)' or 'not found'))
  lines[#lines + 1] = ''
  lines[#lines + 1] = 'workspace roots:'
  if #k.roots == 0 then lines[#lines + 1] = '  none known: .c/.h files are left to Neovim (C); set PURRTTY_KFS_ROOT or root' end
  for _, root in ipairs(k.roots) do
    local r = state.roots[root.keys[1]]
    lines[#lines + 1] = ('  %s (from %s)'):format(root.path, root.source)
    if r then
      local c = client_of(r)
      if c then
        local how = r.candidate and r.candidate.how or '?'
        lines[#lines + 1] = ('    client #%d %s via %s'):format(c.id, c.initialized and 'connected' or 'connecting', how)
        local si = r.server_info
        local t = require('purrtty_kfs.text')
        if si then lines[#lines + 1] = ('    server: %s %s'):format(t.line(si.name or '?'), t.line(si.version or '')) end
        local caps = c.server_capabilities or {}
        local cmds = caps.executeCommandProvider and caps.executeCommandProvider.commands or {}
        lines[#lines + 1] = '    commands: ' .. (#cmds > 0 and table.concat(cmds, ' ') or 'none (diagnostics, hover and completion only)')
        local p = caps.experimental and caps.experimental.purrtty
        if type(p) == 'table' then
          lines[#lines + 1] = ('    purrtty: subscribe=%s vehicleCommands=%s verb=%s'):format(tostring(p.subscribe), tostring(p.vehicleCommands), tostring(p.verb))
        end
      else
        lines[#lines + 1] = '    not connected' .. (r.discovering and ' (looking)' or '') ..
          (r.timer and (', looking again every ' .. cfg.retry_interval_ms .. ' ms') or '')
      end
      local tx = require('purrtty_kfs.text')
      for _, t in ipairs(r.tried) do lines[#lines + 1] = ('    tried %s: %s'):format(tx.line(t.how), tx.line(t.result)) end
      for id, why in pairs(r.refused) do lines[#lines + 1] = ('    refused: %s (%s)'):format(tx.line((id:gsub('|[^|]*$', ''))), tx.line(why)) end
      for id, why in pairs(r.failed) do lines[#lines + 1] = ('    failed: %s (%s)'):format(tx.line((id:gsub('|[^|]*$', ''))), tx.line(why)) end
      lines[#lines + 1] = '    discovery runs: ' .. r.runs
      local st = require('purrtty_kfs.log').state_line(r)
      if st then lines[#lines + 1] = '    program: ' .. st end
    end
  end
  return lines
end

-- ------------------------------------------------------------------ filetypes

local function setup_filetypes()
  vim.filetype.add({
    extension = { kasm = 'kasm', kinc = 'kasm' },
    pattern = {
      -- kC: .c/.h below a kFS workspace root only (checked by a function: Windows paths compare
      -- case-insensitively, symbolic links resolved). Priority beats the .c/.h extension rule.
      ['.*%.[cChH]'] = {
        function(path)
          if M.root_of(path) then return 'kc' end
        end,
        { priority = 1000 },
      },
      -- kASM everywhere: no other filetype owns these extensions (any letter case).
      ['.*%.[kK][aA][sS][mM]'] = 'kasm',
      ['.*%.[kK][iI][nN][cC]'] = 'kasm',
    },
  })
end

-- ------------------------------------------------------------------ commands

local function setup_commands()
  local function cmd(name, fn, opts)
    api.nvim_create_user_command(name, function(a) require('purrtty_kfs.commands')[fn](a) end, opts or {})
  end
  local function complete(list)
    return function(lead)
      return vim.tbl_filter(function(x) return x:find(lead, 1, true) == 1 end, list)
    end
  end
  cmd('KfsBuild', 'build', { nargs = '?', complete = 'file', desc = 'kFS: save the workspace and build a file (kfs build)' })
  cmd('KfsLoad', 'load', { nargs = '?', complete = 'file', desc = 'kFS: save the workspace and load a program into the target' })
  cmd('KfsStart', 'start', { nargs = 0, desc = 'kFS: start the loaded program' })
  cmd('KfsStop', 'stop', { nargs = 0, desc = 'kFS: stop the program' })
  cmd('KfsReset', 'reset', { nargs = 0, desc = 'kFS: reset the computer' })
  cmd('KfsTarget', 'target', { nargs = 1, complete = complete({ 'vehicle', 'bench' }), desc = 'kFS: build target: vehicle or bench' })
  cmd('KfsParts', 'parts', { nargs = '?', complete = complete({ 'lights', 'deploy', 'actuators' }), desc = 'kFS: the vehicle part index tables' })
  cmd('KfsManual', 'manual', { nargs = '?', desc = 'kFS: the manual page of a diagnostic code (default: the one under the cursor)' })
  cmd('KfsVerb', 'verb', { nargs = '+', desc = 'kFS: run any kfs console line' })
  cmd('KfsLog', 'log', { nargs = 0, desc = 'kFS: the program log' })
  cmd('KfsInfo', 'info', { nargs = 0, desc = 'kFS: connection status and how the server was found' })
  cmd('KfsRestart', 'restart', { nargs = 0, desc = 'kFS: reconnect (discover the language server again)' })
end

-- ------------------------------------------------------------------ setup

--- Sets everything up once; later calls only merge `opts` into the configuration.
function M.setup(opts)
  state.setup_calls = state.setup_calls + 1
  if type(opts) == 'table' then state.opts = vim.tbl_deep_extend('force', state.opts, opts) end
  if state.setup_done or state.too_old then return M end
  if vim.fn.has('nvim-' .. M.min_nvim) ~= 1 then
    state.too_old = true
    vim.notify(('purrTTY kFS needs Neovim %s or newer; kC/kASM support is off in this Neovim'):format(M.min_nvim),
      levels.WARN, { title = 'purrtty_kfs' })
    return M
  end
  state.setup_done = true

  env_token = nonempty(vim.env.PURRTTY_KFS_TOKEN)
  vim.env.PURRTTY_KFS_TOKEN = nil

  setup_filetypes()
  setup_commands()

  local group = api.nvim_create_augroup(GROUP, { clear = true })
  api.nvim_create_autocmd('FileType', {
    group = group,
    pattern = { 'kc', 'kasm' },
    desc = 'purrTTY kFS: buffer options and language server attach',
    callback = function(ev)
      local buf = ev.buf
      -- Scheduled: runs after every other FileType handler (the ftplugin and syntax loaders),
      -- whichever order they were defined in.
      vim.schedule(function()
        if not api.nvim_buf_is_valid(buf) then return end
        local ft = vim.bo[buf].filetype
        if ft ~= 'kc' and ft ~= 'kasm' then return end
        if M.config().buffer_options ~= false then require('purrtty_kfs.buffer').setup(buf, ft) end
        M.attach(buf)
      end)
    end,
  })
  api.nvim_create_autocmd('Syntax', {
    group = group,
    pattern = { 'kc', 'kasm' },
    desc = 'purrTTY kFS: C syntax for kC, the shipped kASM syntax by path',
    callback = function(ev)
      local buf = ev.buf
      vim.schedule(function() require('purrtty_kfs.buffer').syntax(buf) end)
    end,
  })
  -- `purr nvim new/dir/file.c` creates nothing; the first :w creates the file's folder — only
  -- below a kFS workspace root, and only for kC/kASM files.
  api.nvim_create_autocmd('BufWritePre', {
    group = group,
    desc = 'purrTTY kFS: create a new workspace file\'s folder',
    callback = function(ev)
      local name = api.nvim_buf_get_name(ev.buf)
      if name == '' or vim.bo[ev.buf].buftype ~= '' then return end
      local ext = name:match('%.([^./\\]+)$')
      if not ext or not ({ c = true, h = true, kasm = true, kinc = true })[ext:lower()] then return end
      local dir = dirname(absolute(name))
      if not dir or is_dir(dir) or not M.root_of(name) then return end
      pcall(vim.fn.mkdir, dir, 'p')
    end,
  })
  api.nvim_create_autocmd('VimLeavePre', {
    group = group,
    desc = 'purrTTY kFS: stop reconnecting',
    callback = function()
      state.exiting = true
      for _, r in pairs(state.roots) do stop_retry(r) end
    end,
  })

  -- Set up late (after start-up)? Then buffers were detected before the rules existed.
  if vim.v.vim_did_enter == 1 then
    vim.schedule(function()
      for _, b in ipairs(api.nvim_list_bufs()) do
        if api.nvim_buf_is_loaded(b) and vim.bo[b].buftype == '' and api.nvim_buf_get_name(b) ~= '' then
          local ft = vim.filetype.match({ buf = b })
          if (ft == 'kc' or ft == 'kasm') and vim.bo[b].filetype ~= ft then vim.bo[b].filetype = ft end
        end
      end
    end)
  end
  return M
end

return M
