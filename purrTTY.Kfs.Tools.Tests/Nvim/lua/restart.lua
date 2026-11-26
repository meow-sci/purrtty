-- The server goes away (game quit) and a new one comes up on another port with another token
-- (the info file): both buffers detach, then reattach to the new server by themselves.
-- Args: second (a second kC file of the workspace).
return function(T)
  local r = {}
  local b1 = vim.api.nvim_get_current_buf()
  local c1 = T.attached(b1)
  r.first = c1.server_info and c1.server_info.name
  r.first_diagnostics = T.wait_diagnostics(b1)
  local b2 = T.edit(T.args.second)
  T.attached(b2)
  vim.cmd.buffer(b1)
  T.signal('attached-a')
  T.wait_signal('a-stopped')
  T.wait('both buffers to detach', function() return T.kfs(b1) == nil and T.kfs(b2) == nil end)
  T.wait('diagnostics to clear', function() return #vim.diagnostic.get(b1) == 0 end)
  T.signal('detached')
  T.wait_signal('b-started')
  T.wait('a reattach to B', function()
    local c = T.kfs(b1)
    return c ~= nil and c.initialized and c.server_info ~= nil and c.server_info.name == 'B'
  end)
  T.attached(b2, 'the second buffer to reattach')
  r.second = T.kfs(b1).server_info.name
  r.same_client = T.kfs(b1).id == T.kfs(b2).id
  r.new_client = T.kfs(b1).id ~= c1.id
  r.diagnostics = T.wait_diagnostics(b1)
  -- :KfsRestart with a live connection: a new client, both buffers on it, nothing announced as lost.
  local before = T.kfs(b1).id
  vim.cmd('KfsRestart')
  T.wait('a new client after :KfsRestart', function()
    local c = T.kfs(b1)
    return c ~= nil and c.initialized and c.id ~= before
  end)
  T.attached(b2, 'the second buffer after :KfsRestart')
  r.restart_same = T.kfs(b1).id == T.kfs(b2).id
  r.gone_messages = #T.messages_with('went away')
  r.back_messages = #T.messages_with('reattached')
  r.none_messages = #T.messages_with('no language server found')
  return r
end
