-- Configured and environment roots are expanded and made absolute: T.args.files are opened and
-- their filetypes returned (kc only below a known root).
return function(T)
  local r = { ft = {} }
  for _, f in ipairs(T.args.files) do
    local b = T.edit(f)
    T.settle()
    r.ft[#r.ft + 1] = vim.bo[b].filetype
  end
  local roots = {}
  for _, x in ipairs(require('purrtty_kfs').info_lines()) do
    if x:match('^  %S.* %(from ') then roots[#roots + 1] = x end
  end
  r.roots = roots
  return r
end
