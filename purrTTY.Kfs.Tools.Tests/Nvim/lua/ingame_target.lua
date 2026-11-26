-- A target change on the game side re-diagnoses the open buffer (run in two Neovims at once).
return function(T)
  local r = {}
  local b = vim.api.nvim_get_current_buf()
  T.attached(b)
  T.wait('KC-E014 on one thruster', function() return T.codes(b) == 'KC-E014' end)
  r.first = T.codes(b)
  T.signal('error-1')
  T.wait_signal('switched-4')
  T.wait('clean on four thrusters', function() return T.codes(b) == '' end)
  T.signal('clean')
  T.wait_signal('switched-2')
  T.wait('KC-E014 on two', function() return T.codes(b) == 'KC-E014' end)
  r.last = T.codes(b)
  return r
end
