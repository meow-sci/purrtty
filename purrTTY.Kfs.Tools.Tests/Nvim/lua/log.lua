-- :KfsLog subscribes, the feeds reach the buffer in order, the cap holds, the tail is followed
-- unless the cursor moved up, the state reaches status(), a trap is announced once, and wiping
-- the buffer unsubscribes. Phases are driven by the C# side (FakeKfsServer.Broadcast).
return function(T)
  local r = {}
  local k = require('purrtty_kfs')
  local src = vim.api.nvim_get_current_buf()
  T.attached(src)
  r.status_connected = k.status() -- subscribes to the state feed
  vim.cmd('KfsLog')
  local lb = vim.fn.bufnr('kfs://log')
  local win = vim.fn.win_findbuf(lb)[1]
  T.signal('log-open')
  T.wait('the first batch', function()
    local l = T.lines(lb)
    return l[#l] == 'three'
  end)
  r.first = T.lines(lb)
  T.wait('the state', function() return k.status():find('running', 1, true) ~= nil end)
  vim.cmd.wincmd('p') -- the source window: status() uses the current buffer's client
  r.status_running = k.status()
  r.followed = vim.api.nvim_win_get_cursor(win)[1] == vim.api.nvim_buf_line_count(lb)
  vim.api.nvim_win_set_cursor(win, { 1, 0 }) -- scrolled up: stop following
  T.signal('scrolled')
  T.wait('the second batch', function()
    local l = T.lines(lb)
    return l[#l] == 'after-trap'
  end)
  r.second = T.lines(lb)
  r.cursor_line = vim.api.nvim_win_get_cursor(win)[1]
  r.trap_messages = #T.messages_with('trap boom')
  r.status_trap = k.status()
  r.modifiable = vim.bo[lb].modifiable
  vim.cmd('bwipeout! ' .. lb)
  T.signal('wiped')
  T.wait_signal('unsubscribed')
  return r
end
