-- The in-game stack: attach through the launcher's environment, diagnostics, and hover and
-- completion on an x_ service through Neovim's own client; then a CRLF copy of the file and a file
-- in a folder whose name has a space and non-ASCII letters. Args: hover_row, hover_col, crlf, unicode.
return function(T)
  local a = T.args
  local r = {}
  local b = vim.api.nvim_get_current_buf()
  local c = T.attached(b)
  r.encoding = c.offset_encoding
  r.diagnostics = T.wait_diagnostics(b)
  r.hover = T.hover(b, a.hover_row, a.hover_col)
  vim.api.nvim_buf_set_lines(b, -1, -1, false, { 'void f(void) { x_li }' })
  r.completion = T.complete(b, vim.api.nvim_buf_line_count(b) - 1, 19)

  local cb = T.edit(a.crlf)
  T.attached(cb)
  r.crlf = { fileformat = vim.bo[cb].fileformat, diagnostics = T.wait_diagnostics(cb), hover = T.hover(cb, a.hover_row, a.hover_col) }

  local ub = T.edit(a.unicode)
  T.attached(ub)
  r.unicode = { diagnostics = T.wait_diagnostics(ub), same_client = T.kfs(ub).id == c.id, uri = vim.uri_from_bufnr(ub) }

  r.kfs_clients = #vim.lsp.get_clients({ name = 'kfs' })
  r.topics = require('purrtty_kfs.log').topics()
  r.info = table.concat(require('purrtty_kfs').info_lines(), '\n')
  return r
end
