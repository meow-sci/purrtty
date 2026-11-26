-- :bdelete / :bunload of kfs://log count as wiping it; :KfsLog then makes a fresh, proper one.
return function(T)
  local log = require('purrtty_kfs.log')
  local r = {}
  for _, how in ipairs({ 'bdelete', 'bunload' }) do
    vim.cmd('KfsLog')
    local b = log.buffer()
    log.append({ 'before ' .. how })
    vim.cmd(how .. '! ' .. b)
    T.settle()
    T.settle()
    log.append({ 'into nothing' })
    local named = {}
    for _, x in ipairs(vim.api.nvim_list_bufs()) do
      if vim.api.nvim_buf_get_name(x) == 'kfs://log' then named[#named + 1] = x end
    end
    vim.cmd('KfsLog')
    local nb = log.buffer()
    log.append({ 'after ' .. how })
    r[how] = {
      gone = log.buffer() ~= b and nb ~= b,
      left = #named,
      buftype = vim.bo[nb].buftype,
      modifiable = vim.bo[nb].modifiable,
      lines = T.lines(nb),
    }
    vim.cmd('only')
  end
  return r
end
