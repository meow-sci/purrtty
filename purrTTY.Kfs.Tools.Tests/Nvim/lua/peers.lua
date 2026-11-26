-- A peer that never answers initialize, or closes before answering: the client is given up
-- (watchdog) or counted, nothing is announced, the candidate is not tried again after
-- max_attempts; a real server appearing (the info file) is then attached.
return function(T)
  local k = require('purrtty_kfs')
  local b = vim.api.nvim_get_current_buf()
  local root
  T.wait('the candidate to be marked failed', function()
    local _, rr = k.client_for(b)
    root = rr
    if not rr then return false end
    for _ in pairs(rr.failed) do return true end
    return false
  end, 60000)
  local failed = {}
  for _, why in pairs(root.failed) do failed[#failed + 1] = why end
  local runs = root.runs
  T.wait('three more discovery runs', function() return root.runs >= runs + 3 and not root.discovering end)
  T.settle()
  T.signal('failed')
  T.wait_signal('server-written')
  local c = T.attached(b, 'the real server via its info file')
  return {
    failed = failed,
    server = c.server_info and c.server_info.name,
    gone_messages = #T.messages_with('went away'),
    connecting = (k.status():find('connecting', 1, true) ~= nil),
  }
end
