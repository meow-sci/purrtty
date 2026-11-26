-- Stale server info files are ignored, then a valid one appears and the buffers attach without
-- any action. Phases are driven by the C# side through the info file.
return function(T)
  local r = {}
  local b = vim.api.nvim_get_current_buf()
  local k = require('purrtty_kfs')
  T.wait('a kc buffer', function() return vim.bo[b].filetype == 'kc' end)
  local root
  local function tried(text)
    for _, t in ipairs(root and root.tried or {}) do
      if t.result:find(text, 1, true) then return true end
    end
    return false
  end
  T.wait('discovery to skip the dead pid', function()
    local _, rr = k.client_for(b)
    root = rr
    return rr ~= nil and not rr.discovering and tried('is not running')
  end)
  r.clients_dead_pid = #vim.lsp.get_clients({ bufnr = b })
  T.signal('dead-pid-skipped')
  T.wait_signal('refused-port-written')
  T.wait('discovery to skip the refused port', function() return not root.discovering and tried('no server at') end)
  r.clients_refused = #vim.lsp.get_clients({ bufnr = b })
  T.signal('refused-port-skipped')
  T.wait_signal('good-written')
  local c = T.attached(b, 'the buffer to attach by itself')
  r.server = c.server_info and c.server_info.name
  r.diagnostics = T.wait_diagnostics(b)
  r.none_messages = #T.messages_with('no language server found')
  r.kind = root.candidate.kind
  return r
end
