-- Server strings with newlines, escape sequences, control characters and very long lines reach
-- the log, the messages and the status line cleaned; nothing raises. Phases driven by the C# side.
return function(T)
  local k = require('purrtty_kfs')
  local src = vim.api.nvim_get_current_buf()
  T.attached(src)
  k.status()
  vim.cmd('KfsLog')
  local lb = require('purrtty_kfs.log').buffer()
  vim.cmd.wincmd('p')
  T.signal('ready')
  T.wait('the last line', function()
    local l = T.lines(lb)
    return l[#l] == 'end'
  end)
  T.wait('the trap in the status', function() return k.status():find('trap', 1, true) ~= nil end)
  local errors = {}
  for _, m in ipairs(T.messages) do
    if m.level >= vim.log.levels.ERROR and not m.msg:find('trap', 1, true) then errors[#errors + 1] = m.msg end
  end
  return {
    lines = T.lines(lb),
    status = k.status(),
    trap_messages = T.messages_with('trap DIV_ZERO'),
    errors = errors,
  }
end
