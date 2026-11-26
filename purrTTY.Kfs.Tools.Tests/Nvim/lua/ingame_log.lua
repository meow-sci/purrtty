-- :KfsLog against the in-game feed: nothing subscribed at attach; the log's tail, then new lines
-- from the live source and a command's console output (the tee), in order; the state in status();
-- a trap as a diagnostic in the buffer, cleared when the program runs again.
return function(T)
  local r = {}
  local k = require('purrtty_kfs')
  local log = require('purrtty_kfs.log')
  local b = vim.api.nvim_get_current_buf()
  T.attached(b)
  r.topics_at_attach = log.topics()
  T.signal('attached')
  T.wait_signal('checked-idle')

  vim.cmd('KfsLog')
  local lb = vim.fn.bufnr('kfs://log')
  vim.cmd.wincmd('p') -- back to the source window: status() follows the current buffer's client
  T.wait('the tail', function() return #T.lines(lb) >= 4 end)
  r.tail = T.lines(lb)
  T.signal('tail-shown')
  T.wait('new 1', function() local l = T.lines(lb); return l[#l] == '[k 1 t 0.10] new 1' end)
  r.start = T.command('KfsStart')
  T.signal('started')
  T.wait('new 2', function() local l = T.lines(lb); return l[#l] == '[k 2 t 0.20] new 2' end)
  r.log = T.lines(lb)

  T.wait('the state', function() return k.status():find('Running', 1, true) ~= nil end)
  r.status_running = k.status()
  T.signal('state-seen')
  T.wait('the trap diagnostic', function() return T.codes(b):find('KFS-TRAP', 1, true) ~= nil end)
  r.trap = T.diagnostics(b)
  r.trap_message = vim.diagnostic.get(b)[1].message
  r.status_trap = k.status()
  T.signal('trap-shown')
  T.wait('the trap to clear', function() return T.codes(b) == '' end)
  r.trap_messages = #T.messages_with('trap DIV_ZERO in task 0, function run (')
  r.log_end = T.lines(lb)
  return r
end
