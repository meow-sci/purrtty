-- purrTTY kFS: making text from a language server (or its info file) safe to show.
--
-- A buffer line cannot hold a newline (nvim_buf_set_lines raises), a notification or a status
-- line should be one line, and terminal escape sequences or other control characters from a
-- program's log must not reach the screen as they are. Very long lines are cut.

local M = {}

--- The longest line kept, in bytes; longer ones are cut and end in '...'.
M.max_line = 2000

local function strip(s)
  -- Escape sequences: CSI (ESC [ ... final), OSC (ESC ] ... BEL or ESC \), any other ESC + one char.
  s = s:gsub('\27%[[\48-\63]*[\32-\47]*[\64-\126]', '')
  s = s:gsub('\27%][^\7\27]*\7', ''):gsub('\27%][^\7\27]*\27\\', '')
  s = s:gsub('\27.?', '')
  s = s:gsub('\155[\48-\63]*[\32-\47]*[\64-\126]', '') -- an 8-bit CSI
  -- Other control characters (a tab stays) become one '?' each.
  s = s:gsub('[%z\1-\8\11-\31\127]', '?')
  return s
end

local function cut(s)
  if #s <= M.max_line then return s end
  -- Cut on a character boundary: drop a trailing incomplete UTF-8 sequence.
  local head = s:sub(1, M.max_line):gsub('[\192-\255][\128-\191]*$', '')
  return head .. '...'
end

--- One displayable line: newlines (\r, \n, \r\n) become spaces, escapes and controls are removed.
function M.line(s)
  if s == nil or s == vim.NIL then return '' end
  s = tostring(s):gsub('\r\n', ' '):gsub('[\r\n]', ' ')
  return cut(strip(s))
end

--- Displayable lines: split at \r\n, \r and \n (a trailing newline adds no empty line), each cleaned.
function M.lines(s)
  local out = {}
  if s == nil or s == vim.NIL then return out end
  s = tostring(s)
  if s == '' then return out end
  s = s:gsub('\r\n', '\n'):gsub('\r', '\n')
  for l in (s .. '\n'):gmatch('(.-)\n') do out[#out + 1] = cut(strip(l)) end
  if s:sub(-1) == '\n' then out[#out] = nil end
  return out
end

return M
