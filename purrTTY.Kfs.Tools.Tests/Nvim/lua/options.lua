-- buffer_options = false: the kc buffer keeps the options it had (no purrTTY buffer setup) and
-- still attaches.
return function(T)
  local b = vim.api.nvim_get_current_buf()
  T.attached(b)
  T.settle()
  return { ft = vim.bo[b].filetype, marker = vim.b[b].purrtty_kfs, cindent = vim.bo[b].cindent, commentstring = vim.bo[b].commentstring }
end
