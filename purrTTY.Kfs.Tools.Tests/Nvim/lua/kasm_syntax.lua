-- The shipped kASM syntax on the lines of T.args.probes: { line, col, text } (1-based), each
-- answered with the syntax group at that place and the text found there.
return function(T)
  local b = vim.api.nvim_get_current_buf()
  T.wait('kasm syntax', function() return vim.b[b].current_syntax == 'kasm' end)
  local r = { groups = {} }
  for _, p in ipairs(T.args.probes) do
    local line = vim.api.nvim_buf_get_lines(b, p[1] - 1, p[1], false)[1] or ''
    r.groups[#r.groups + 1] = {
      at = p[1] .. ':' .. p[2],
      text = line:sub(p[2], p[2] + #p[3] - 1),
      group = vim.fn.synIDattr(vim.fn.synID(p[1], p[2], 1), 'name'),
    }
  end
  return r
end
