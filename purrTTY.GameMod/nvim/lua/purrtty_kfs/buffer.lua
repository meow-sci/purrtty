-- purrTTY kFS: per-buffer options and syntax for the kc and kasm filetypes.
--
-- Set from Lua (scheduled after every FileType/Syntax handler) instead of ftplugin/ and syntax/
-- files, so nothing depends on 'runtimepath'. Only buffer- and window-local options of kc/kasm
-- buffers are touched.

local M = {}

local api = vim.api
local registered = false

local function kfs()
  return require('purrtty_kfs')
end

--- The shipped kASM syntax file.
function M.kasm_syntax_file()
  return kfs()._state.dir .. '/syntax/kasm.vim'
end

--- Options for a kc or kasm buffer.
function M.setup(buf, ft)
  local bo = vim.bo[buf]
  if ft == 'kc' then
    bo.commentstring = '// %s'
    bo.comments = 's1:/*,mb:*,ex:*/,://'
    bo.cindent = true
    bo.suffixesadd = '.h,.c'
    if not registered then
      registered = true
      -- The C tree-sitter parser serves kC (Neovim ships it); a player's tree-sitter set-up then
      -- treats kc like c. Highlighting itself is started only on request (treesitter = true).
      pcall(vim.treesitter.language.register, 'c', 'kc')
    end
    if kfs().config().treesitter then pcall(vim.treesitter.start, buf, 'c') end
  else
    bo.commentstring = '; %s'
    bo.comments = ':;'
    bo.cindent = false
    bo.smartindent = false
    bo.autoindent = true
    bo.suffixesadd = '.kasm,.kinc'
  end
  vim.b[buf].purrtty_kfs = ft
end

--- Runs after a Syntax event for kc/kasm: kC gets Neovim's C syntax; kASM gets the shipped
--- syntax file, sourced by path unless a syntax/kasm.vim on 'runtimepath' already loaded.
function M.syntax(buf)
  if not api.nvim_buf_is_valid(buf) then return end
  local syn = vim.bo[buf].syntax
  if syn == 'kc' then
    api.nvim_buf_call(buf, function() vim.bo.syntax = 'c' end)
  elseif syn == 'kasm' and vim.b[buf].current_syntax ~= 'kasm' then
    api.nvim_buf_call(buf, function()
      vim.cmd('syntax clear')
      vim.b.current_syntax = nil
      vim.cmd('source ' .. vim.fn.fnameescape(M.kasm_syntax_file()))
    end)
  end
end

return M
