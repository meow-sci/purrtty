-- A wrong token: one clear message, no retry against that server.
return function(T)
  local r = {}
  local b = vim.api.nvim_get_current_buf()
  local k = require('purrtty_kfs')
  T.wait('the refusal message', function() return #T.messages_with('refused this session') > 0 end)
  local _, root = k.client_for(b)
  local runs = root.runs
  T.wait('five more discovery runs', function() return root.runs >= runs + 5 and not root.discovering end)
  T.settle()
  r.refused_messages = #T.messages_with('refused this session')
  r.none_messages = #T.messages_with('no language server found')
  r.clients = #vim.lsp.get_clients({ bufnr = b })
  r.tried = root.tried
  -- :KfsRestart forgets the refusal and tries again (the server sees a second initialize).
  vim.cmd('KfsRestart')
  T.wait('a second refusal after :KfsRestart', function() return #T.messages_with('refused this session') == 2 end)
  r.refused_after_restart = #T.messages_with('refused this session')
  r.errors = {}
  for _, m in ipairs(T.messages) do
    if m.level >= vim.log.levels.ERROR then r.errors[#r.errors + 1] = m.msg end
  end
  return r
end
