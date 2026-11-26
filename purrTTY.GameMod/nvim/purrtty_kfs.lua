-- purrTTY kFS for Neovim: the entry script.
--
-- The purrTTY mod starts Neovim with
--   --cmd "lua dofile(vim.env.PURRTTY_KFS_NVIM .. '/purrtty_kfs.lua')"
-- so this file is loaded by absolute path, before the player's own configuration, from a folder
-- whose path may contain spaces. It makes ./lua requirable relative to its own location (no
-- 'runtimepath' change) and runs setup(). Outside the game the same line works by hand:
--   nvim --cmd "lua dofile('/path/to/purrTTY/nvim/purrtty_kfs.lua')" file.c
-- See doc/purrtty-kfs.txt.

local source = debug.getinfo(1, 'S').source
if source:sub(1, 1) == '@' then source = source:sub(2) end
local dir = source:gsub('\\', '/'):match('^(.*)/[^/]*$') or '.'
local templates = dir .. '/lua/?.lua;' .. dir .. '/lua/?/init.lua'
if not package.path:find(templates, 1, true) then
  package.path = templates .. ';' .. package.path
end

local ok, kfs = pcall(require, 'purrtty_kfs')
if ok then
  kfs.setup()
else
  vim.notify('purrTTY kFS: could not load ' .. dir .. '/lua/purrtty_kfs: ' .. tostring(kfs), vim.log.levels.ERROR)
end
