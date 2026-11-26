-- Shared driver for the headless attach scripts (Neovim 0.12).
-- Usage from attach_*.lua:  require-free: dofile(<dir>/common.lua).run{ label=, cmd=, files=, hover=, rapid= }
local M = {}

local function out(s) io.stdout:write(s .. '\n') end
M.out = out

--- kC files are '.c': give them their own filetype by PATH so clangd (filetypes c/cpp) never attaches.
function M.setup_filetype()
  vim.cmd('filetype on')
  vim.filetype.add({
    pattern = {
      -- the game's workspace: <Documents>/My Games/Kitten Space Agency/kfs/**.c|h
      ['.*/My Games/Kitten Space Agency/kfs/.*%.[ch]'] = 'kc',
      -- this spike's samples
      ['.*/05%-lsp%-neovim/samples/.*%.c'] = 'kc',
    },
  })
end

M.started = {} -- client ids returned by vim.lsp.start, in order
M.published = {} -- uri -> list of publishDiagnostics params, in arrival order

local function on_publish(err, params, ctx)
  local l = M.published[params.uri] or {}
  M.published[params.uri] = l
  table.insert(l, params)
  return vim.lsp.diagnostic.on_publish_diagnostics(err, params, ctx)
end

--- Start (or reuse) the 'kfs' client for kc buffers only.
function M.install_autocmd(cmd, extra)
  vim.api.nvim_create_autocmd('FileType', {
    pattern = 'kc',
    callback = function(ev)
      M.started[#M.started + 1] = vim.lsp.start(vim.tbl_extend('force', {
        name = 'kfs',
        cmd = cmd, -- vim.lsp.rpc.connect(host, port) for TCP, or { 'dotnet', dll, '--stdio' }
        root_dir = vim.fs.dirname(vim.fs.abspath(ev.file)),
        handlers = { ['textDocument/publishDiagnostics'] = on_publish },
        flags = { debounce_text_changes = 0 }, -- send every change at once (stresses supersession)
      }, extra or {}), { bufnr = ev.buf })
    end,
  })
end

local function client_for(buf)
  return vim.lsp.get_clients({ bufnr = buf, name = 'kfs' })[1]
end

local function wait_publish(uri, min_count, timeout)
  return vim.wait(timeout or 20000, function()
    return #(M.published[uri] or {}) >= min_count
  end, 10)
end

M.wait_publish = wait_publish

local function first_line(s) return (s:gsub('\n.*', '')) end

function M.report(buf)
  local uri = vim.uri_from_bufnr(buf)
  local pubs = M.published[uri] or {}
  local last = pubs[#pubs]
  local raw = {}
  for _, d in ipairs(last and last.diagnostics or {}) do
    table.insert(raw, { range = { d.range.start.line, d.range.start.character, d.range['end'].line, d.range['end'].character },
      severity = d.severity, code = d.code, source = d.source, message = d.message })
  end
  local nv = {}
  for _, d in ipairs(vim.diagnostic.get(buf)) do
    table.insert(nv, { lnum = d.lnum, col = d.col, end_col = d.end_col, severity = d.severity, code = d.code, msg = first_line(d.message) })
  end
  local c = client_for(buf)
  return {
    file = vim.fs.basename(vim.api.nvim_buf_get_name(buf)),
    filetype = vim.bo[buf].filetype,
    clients = vim.tbl_map(function(x) return x.name end, vim.lsp.get_clients({ bufnr = buf })),
    encoding = c and c.offset_encoding,
    version = last and last.version,
    lsp_diagnostics = raw,
    nvim_diagnostics = nv,
  }
end

local function unicode_check(buf, rep)
  for i, line in ipairs(vim.api.nvim_buf_get_lines(buf, 0, -1, false)) do
    local s = line:find('oops_undeclared', 1, true)
    if s then
      local got = rep.nvim_diagnostics[1]
      local ok = got and got.lnum == i - 1 and got.col == s - 1 and got.end_col == s - 1 + #'oops_undeclared'
      out(string.format('UNICODE-CHECK encoding=%s expected lnum=%d col=%d end_col=%d (bytes) got lnum=%s col=%s end_col=%s raw_character=%s -> %s',
        rep.encoding, i - 1, s - 1, s - 1 + #'oops_undeclared', got and got.lnum, got and got.col, got and got.end_col,
        rep.lsp_diagnostics[1] and rep.lsp_diagnostics[1].range[2], ok and 'PASS' or 'FAIL'))
    end
  end
end

local function request_at(buf, word, method)
  local c = client_for(buf)
  for i, line in ipairs(vim.api.nvim_buf_get_lines(buf, 0, -1, false)) do
    local s = line:find(word, 1, true)
    if s then
      vim.api.nvim_win_set_cursor(0, { i, s + 1 })
      local params = vim.lsp.util.make_position_params(0, c.offset_encoding)
      local res, err = c:request_sync(method, params, 10000, buf)
      return res and res.result, err or (res and res.err)
    end
  end
end

function M.run(cfg)
  M.setup_filetype()
  M.install_autocmd(cfg.cmd)
  local t0 = vim.uv.hrtime()
  for _, f in ipairs(cfg.files) do
    vim.cmd.edit(vim.fn.fnameescape(f))
    local buf = vim.api.nvim_get_current_buf()
    local got = wait_publish(vim.uri_from_bufnr(buf), 1)
    local rep = M.report(buf)
    rep.waited_ok = got
    out('DIAGNOSTICS ' .. vim.json.encode(rep))
    if rep.file == 'unicode.c' then unicode_check(buf, rep) end
  end
  out(string.format('(all files: %.0f ms incl. first connect/initialize)', (vim.uv.hrtime() - t0) / 1e6))

  if cfg.hover then
    vim.cmd.edit(vim.fn.fnameescape(cfg.hover))
    local buf = vim.api.nvim_get_current_buf()
    wait_publish(vim.uri_from_bufnr(buf), 1)
    for _, w in ipairs({ 'purr_light_set_on', 'kfs_logf' }) do
      local r, e = request_at(buf, w, 'textDocument/hover')
      out('HOVER ' .. w .. ' ' .. vim.json.encode({ error = e, contents = r and r.contents, range = r and r.range }))
    end
    local r, e = request_at(buf, 'purr_light_count', 'textDocument/completion')
    local items = r and (r.items or r) or {}
    local labels, mine = {}, {}
    for _, it in ipairs(items) do
      table.insert(labels, it.label)
      if it.label:match('^purr_') then table.insert(mine, { label = it.label, detail = it.detail, documentation = it.documentation }) end
    end
    out(string.format('COMPLETION error=%s count=%d labels=%s', vim.inspect(e), #items, table.concat(labels, ',')))
    out('COMPLETION purr items ' .. vim.json.encode(mine))

    if cfg.rapid then
      -- Fire several full-text didChange notifications back to back (debounce 0), ending on a valid text.
      local uri = vim.uri_from_bufnr(buf)
      local before = #(M.published[uri] or {})
      local orig = vim.api.nvim_buf_get_lines(buf, 0, -1, false)
      local n = #orig
      for i = 1, 8 do
        vim.api.nvim_buf_set_lines(buf, n - 1, n - 1, false, { '    bogus_' .. i .. '();' })
      end
      vim.api.nvim_buf_set_lines(buf, 0, -1, false, orig) -- final edit: valid again
      local final_version = vim.lsp.util.buf_versions[buf]
      vim.wait(20000, function()
        local l = M.published[uri]
        return l[#l].version == final_version
      end, 10)
      vim.wait(1500, function() return false end) -- allow stale publishes (if any) to arrive late
      local seq = {}
      for i = before + 1, #M.published[uri] do
        local p = M.published[uri][i]
        table.insert(seq, string.format('v%s:%d', tostring(p.version), #p.diagnostics))
      end
      local last = M.published[uri][#M.published[uri]]
      out(string.format('RAPID-EDITS final_version=%d publishes_after_edits=[%s] last=v%s with %d diagnostics -> %s',
        final_version, table.concat(seq, ' '), tostring(last.version), #last.diagnostics,
        (last.version == final_version and #last.diagnostics == 0) and 'PASS' or 'FAIL'))
    end
  end

  for _, c in ipairs(vim.lsp.get_clients({ name = 'kfs' })) do c:stop() end
  vim.wait(5000, function() return #vim.lsp.get_clients({ name = 'kfs' }) == 0 end, 10)
  out('clients after stop: ' .. #vim.lsp.get_clients({ name = 'kfs' }))
end

return M
