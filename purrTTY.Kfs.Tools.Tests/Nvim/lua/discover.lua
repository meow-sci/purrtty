-- Discovery: the launcher (or a hand launch) opened a kC file; wait for a kfs client and its
-- diagnostics, report how the server was found. Args: commands (also try :KfsBuild and :KfsLog
-- against a server without the in-game capabilities).
return function(T)
  local r = {}
  local b = vim.api.nvim_get_current_buf()
  r.ft = vim.bo[b].filetype
  local c = T.attached(b)
  r.diagnostics = T.wait_diagnostics(b)
  local k = require('purrtty_kfs')
  local _, root = k.client_for(b)
  r.kind = root.candidate.kind
  r.how = root.candidate.how
  r.root = root.path
  r.server = c.server_info and c.server_info.name
  if T.args.commands then
    vim.api.nvim_buf_set_lines(b, -1, -1, false, { '// unsaved' })
    r.build = T.command('KfsBuild')
    r.modified_after_build = vim.bo[b].modified
    r.manual = T.command('KfsManual KC-E003')
    vim.cmd('KfsLog')
    T.settle()
    r.log_messages = T.messages_with('sends no program log')
  end
  r.info = table.concat(k.info_lines(), '\n')
  return r
end
