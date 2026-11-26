-- Neovim_Attaches_Headless: the launcher opened bad.c; then ok.c, prog.kasm and a C file outside
-- the workspace. Args: ok, kasm, outside (absolute paths).
return function(T)
  local a = T.args
  local r = {}
  local b1 = vim.api.nvim_get_current_buf()
  local c1 = T.attached(b1)
  r.bad = { ft = vim.bo[b1].filetype, diagnostics = T.wait_diagnostics(b1), encoding = c1.offset_encoding }
  T.wait('kc buffer options', function() return vim.b[b1].purrtty_kfs == 'kc' and vim.bo[b1].syntax == 'c' end)
  r.kc = { syntax = vim.bo[b1].syntax, commentstring = vim.bo[b1].commentstring, cindent = vim.bo[b1].cindent }

  local b2 = T.edit(a.ok)
  local c2 = T.attached(b2)
  r.ok = { ft = vim.bo[b2].filetype, same_client = c1.id == c2.id }

  local b3 = T.edit(a.kasm)
  local c3 = T.attached(b3)
  r.kasm = { ft = vim.bo[b3].filetype, same_client = c1.id == c3.id, diagnostics = T.wait_diagnostics(b3) }
  T.wait('kasm syntax', function() return vim.b[b3].current_syntax == 'kasm' and vim.b[b3].purrtty_kfs == 'kasm' end)
  r.kasm.commentstring = vim.bo[b3].commentstring
  r.kasm.syntax = vim.bo[b3].syntax
  local function group(l, c) return vim.fn.synIDattr(vim.fn.synID(l, c, 1), 'name') end
  r.kasm.groups = { directive = group(1, 1), mnemonic = group(5, 5), constant = group(5, 12), comment = group(9, 3) }

  local b4 = T.edit(a.outside)
  T.settle()
  T.settle()
  r.outside = { ft = vim.bo[b4].filetype, clients = #vim.lsp.get_clients({ bufnr = b4 }), syntax = vim.bo[b4].syntax }

  local b5 = T.edit(a.outside_kasm)
  T.settle()
  T.settle()
  r.outside_kasm = { ft = vim.bo[b5].filetype, clients = #vim.lsp.get_clients({ bufnr = b5 }) }
  r.treesitter_lang = vim.treesitter.language.get_lang('kc')

  r.kfs_clients = #vim.lsp.get_clients({ name = 'kfs' })
  r.status = require('purrtty_kfs').status()
  r.info = table.concat(require('purrtty_kfs').info_lines(), '\n')
  r.autocmds = T.autocmd_count()
  r.commands = T.user_commands()
  r.setup_calls = require('purrtty_kfs')._state.setup_calls
  r.token_env = vim.env.PURRTTY_KFS_TOKEN
  r.state_text = vim.inspect(require('purrtty_kfs')._state)
  return r
end
