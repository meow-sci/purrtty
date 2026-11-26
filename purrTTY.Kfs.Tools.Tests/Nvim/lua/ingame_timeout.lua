-- A command while the game's main thread does not pump: the bounded wait answers, Neovim and the
-- server stay responsive meanwhile, and the next command works once the frames run again.
-- Args: hover_row, hover_col.
return function(T)
  local r = {}
  local b = vim.api.nvim_get_current_buf()
  T.attached(b)
  T.wait_diagnostics(b)
  T.signal('ready')
  T.wait_signal('not-pumping')
  local n = #T.done
  local t0 = vim.uv.hrtime()
  vim.cmd('KfsStart')
  r.hover_while_waiting = T.hover(b, T.args.hover_row, T.args.hover_col) ~= nil
  r.pending_after_hover = #T.done == n
  T.wait('the bounded wait', function() return #T.done > n end, 40000)
  r.start = T.done[n + 1]
  r.waited_ms = (vim.uv.hrtime() - t0) / 1e6
  T.signal('timed-out')
  T.wait_signal('pumping')
  r.stop = T.command('KfsStop')
  return r
end
