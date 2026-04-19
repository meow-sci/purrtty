The custom game shell is not displaying any output.

here's the logging we're getting:

```
[GameConsoleShell.SendOutput(string)] text='
'
[GameConsoleShell.SendOutput(byte[])] data.Length=2, OutputReceived listener count=1
08:26:50  INFO set simulation speed to x1.2
```

this shows that we are getting the output in OutputReceived as expected, but we are not displaying it in our custom game shell output!

debug the issue and fix the problem
