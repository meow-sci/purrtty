-- Loaded through the entry script and/or as a plugin: one set of autocommands and commands, the
-- kc filetype and a working attach either way.
return function(T)
  local r = {}
  local b = vim.api.nvim_get_current_buf()
  local c = T.attached(b)
  r.ft = vim.bo[b].filetype
  r.diagnostics = T.wait_diagnostics(b)
  r.kfs_clients = #vim.lsp.get_clients({ name = 'kfs' })
  r.client = c.id
  local k = require('purrtty_kfs')
  r.setup_calls = k._state.setup_calls
  r.autocmds = T.autocmd_count()
  r.commands = T.user_commands()
  r.plugin_files = #vim.api.nvim_get_runtime_file('plugin/purrtty_kfs.lua', true)
  r.module_dir = k._state.dir
  r.loadplugins = vim.o.loadplugins
  return r
end
