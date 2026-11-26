-- Nothing available: plain kc buffer, one message, commands say so, retries stay quiet.
return function(T)
  local r = {}
  local b = vim.api.nvim_get_current_buf()
  local k = require('purrtty_kfs')
  T.wait('a kc buffer', function() return vim.b[b].purrtty_kfs == 'kc' end)
  local root
  T.wait('a failed discovery', function()
    local _, rr = k.client_for(b)
    root = rr
    return rr ~= nil and rr.runs >= 1 and not rr.discovering
  end)
  local runs = root.runs
  T.wait('three more discovery runs', function() return root.runs >= runs + 3 and not root.discovering end)
  T.settle()
  r.ft = vim.bo[b].filetype
  r.syntax = vim.bo[b].syntax
  r.clients = #vim.lsp.get_clients({ bufnr = b })
  r.none_messages = #T.messages_with('no language server found')
  r.build = T.command('KfsBuild')
  r.info = table.concat(k.info_lines(), '\n')
  r.timer_while_open = root.timer ~= nil
  -- No kFS buffer left: the reconnect timer stops.
  vim.cmd('enew')
  vim.cmd('bwipeout! ' .. b)
  T.wait('the reconnect timer to stop', function() return root.timer == nil end)
  r.runs_at_wipe = root.runs
  return r
end
