-- Discovery of the in-game server and `purr lsp restart` (new port and token): the client sees
-- the server go, finds the new one (the info file under PURRTTY_CONFIG_DIR) and its commands work
-- again. Args: launched_without_server (the service started after Neovim: wait for discovery to
-- fail once first).
return function(T)
  local r = {}
  local k = require('purrtty_kfs')
  local b = vim.api.nvim_get_current_buf()
  if T.args.launched_without_server then
    T.wait('a failed discovery', function()
      local _, rr = k.client_for(b)
      return rr ~= nil and rr.runs >= 1 and not rr.discovering
    end)
    T.signal('looked')
  end
  local c1 = T.attached(b, 'the first server')
  local _, root = k.client_for(b)
  r.first_how = root.candidate.how
  r.diagnostics = T.wait_diagnostics(b)
  r.start1 = T.command('KfsStart')
  T.signal('attached-1')
  T.wait_signal('restarted')
  T.wait('a new client', function()
    local c = T.kfs(b)
    return c ~= nil and c.initialized and c.id ~= c1.id
  end)
  _, root = k.client_for(b)
  r.second_how = root.candidate.how
  r.tried = root.tried
  r.diagnostics2 = T.wait_diagnostics(b)
  r.verb = T.command('KfsVerb state')
  r.start2 = T.command('KfsStart')
  r.gone = #T.messages_with('went away')
  r.back = #T.messages_with('reattached')
  r.refused = #T.messages_with('refused')
  r.none = #T.messages_with('no language server found')
  return r
end
