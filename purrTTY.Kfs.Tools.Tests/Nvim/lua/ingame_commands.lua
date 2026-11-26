-- Every :Kfs* command against the in-game server; the fake game records each line on the thread
-- that pumps (the test's). Args: header (x_lights/util.h), spaced (my prog/main.c).
return function(T)
  local r = {}
  local log = require('purrtty_kfs.log')
  local a = vim.api.nvim_get_current_buf()
  T.attached(a)
  local h = T.edit(T.args.header)
  T.attached(h)
  vim.api.nvim_buf_set_lines(a, -1, -1, false, { '// edited in main.c' })
  vim.api.nvim_buf_set_lines(h, -1, -1, false, { '// edited in util.h' })
  vim.cmd.buffer(a)
  r.topics_at_attach = log.topics()

  r.build = T.command('KfsBuild')
  r.build_output = T.lines(vim.fn.bufnr('kfs://output'))
  r.modified_after_build = { a = vim.bo[a].modified, h = vim.bo[h].modified }
  r.topics_after_build = log.topics()
  r.load = T.command('KfsLoad')
  r.topics_after_load = log.topics()
  r.start = T.command('KfsStart')
  r.stop = T.command('KfsStop')
  r.reset = T.command('KfsReset')
  r.target = T.command('KfsTarget vehicle')
  r.load_vehicle = T.command('KfsLoad')
  r.parts = T.command('KfsParts lights')
  r.manual = T.command('KfsManual KC-E003')
  r.manual_output = T.lines(vim.fn.bufnr('kfs://output'))

  local s = T.edit(T.args.spaced)
  T.attached(s)
  r.load_spaced = T.command('KfsLoad')
  vim.api.nvim_buf_set_lines(s, -1, -1, false, { '// not written for a verb the server refuses' })
  r.verb = T.command('KfsVerb state')
  r.spaced_modified_after_verb = vim.bo[s].modified
  return r
end
