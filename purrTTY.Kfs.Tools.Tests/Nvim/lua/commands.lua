-- Every :Kfs* command against the fake in-game handlers. The launcher opened a.c (with an error
-- for :KfsManual's default). Args: header (b.h of the workspace), outside (a C file elsewhere).
return function(T)
  local r = {}
  local a = vim.api.nvim_get_current_buf()
  T.attached(a)
  T.wait_diagnostics(a)
  local b = T.edit(T.args.header)
  T.attached(b)
  local o = T.edit(T.args.outside)
  -- Unsaved edits everywhere: the workspace ones must reach the disk before build/load/verb.
  vim.api.nvim_buf_set_lines(a, -1, -1, false, { '// edited in a.c' })
  vim.api.nvim_buf_set_lines(b, -1, -1, false, { '// edited in b.h' })
  vim.api.nvim_buf_set_lines(o, -1, -1, false, { '// edited outside' })
  vim.cmd.buffer(a)

  r.build = T.command('KfsBuild')
  r.modified_after_build = { a = vim.bo[a].modified, b = vim.bo[b].modified, outside = vim.bo[o].modified }
  r.load = T.command('KfsLoad')
  r.load_output = T.lines(vim.fn.bufnr('kfs://output'))
  r.start = T.command('KfsStart')
  r.stop = T.command('KfsStop')
  r.reset = T.command('KfsReset')
  r.target = T.command('KfsTarget bench')
  r.target_bad = T.command('KfsTarget moon')
  r.parts_all = T.command('KfsParts')
  r.parts = T.command('KfsParts lights')
  r.parts_output = T.lines(vim.fn.bufnr('kfs://output'))
  r.current_is_source = vim.api.nvim_get_current_buf() == a
  vim.api.nvim_win_set_cursor(0, { 5, 23 }) -- on 'missing'
  r.manual_cursor = T.command('KfsManual')
  r.manual = T.command('KfsManual KA-E001')
  r.verb = T.command('KfsVerb status')
  -- From a buffer that is not a kFS file: the only kFS client is used.
  vim.cmd.buffer(o)
  r.from_outside = T.command('KfsStart')
  r.modified_at_end = { outside = vim.bo[o].modified }
  return r
end
