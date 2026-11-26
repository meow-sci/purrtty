-- A new workspace file in folders that do not exist yet: the first :w creates them (only inside
-- the workspace). Args: inside, outside (absolute paths of files whose folders are missing).
return function(T)
  local r = {}
  local b = vim.api.nvim_get_current_buf() -- the launcher opened T.args.inside
  vim.api.nvim_buf_set_lines(b, 0, -1, false, { 'int x;' })
  r.inside_ok = pcall(vim.cmd, 'write')
  r.inside_exists = vim.uv.fs_stat(T.args.inside) ~= nil
  local o = T.edit(T.args.outside)
  vim.api.nvim_buf_set_lines(o, 0, -1, false, { 'int y;' })
  r.outside_ok = pcall(vim.cmd, 'write')
  r.outside_dir = vim.uv.fs_stat(vim.fs.dirname(T.args.outside)) ~= nil
  return r
end
