-- :KfsBuild <file> sends that file's URI, whatever other buffers are named like it.
-- Args: loaded (files to load first), build (files to build, in order).
return function(T)
  local first = vim.api.nvim_get_current_buf()
  T.attached(first)
  for _, f in ipairs(T.args.loaded) do T.edit(f) end
  vim.cmd.buffer(first)
  local r = { results = {} }
  for _, f in ipairs(T.args.build) do
    r.results[#r.results + 1] = T.command('KfsBuild ' .. vim.fn.fnameescape(f))
  end
  return r
end
