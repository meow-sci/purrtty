-- A Neovim older than the runtime files support: one message, nothing set up.
return function(T)
  local r = {}
  local b = vim.api.nvim_get_current_buf()
  T.settle()
  r.ft = vim.bo[b].filetype
  r.autocmds = T.autocmd_count()
  r.commands = T.user_commands()
  r.messages_old = #T.messages_with('needs Neovim')
  r.clients = #vim.lsp.get_clients({ bufnr = b })
  return r
end
