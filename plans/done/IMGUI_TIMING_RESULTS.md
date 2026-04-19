# baseline from main
┌────────────────────────────────────────────────────────────────┬──────────────┬─────────┬──────────────┬──────────────┐
│ Task Name                                                      │ Total (ms)   │ Count   │ Avg (ms)     │ Avg (µs)     │
├────────────────────────────────────────────────────────────────┼──────────────┼─────────┼──────────────┼──────────────┤
│ TerminalController.Render                                      │       196.49 │      60 │        3.275 │      3274.87 │
└────────────────────────────────────────────────────────────────┴──────────────┴─────────┴──────────────┴──────────────┘

# current

┌────────────────────────────────────────────────────────────────┬──────────────┬─────────┬──────────────┬──────────────┐
│ Task Name                                                      │ Total (ms)   │ Count   │ Avg (ms)     │ Avg (µs)     │
├────────────────────────────────────────────────────────────────┼──────────────┼─────────┼──────────────┼──────────────┤
│ TerminalController.Render                                      │       104.98 │      60 │        1.750 │      1749.74 │
└────────────────────────────────────────────────────────────────┴──────────────┴─────────┴──────────────┴──────────────┘

# current detailed (NOTE: timings themselves slow it down a lot)

┌────────────────────────────────────────────────────────────────┬──────────────┬─────────┬──────────────┬──────────────┐
│ Task Name                                                      │ Total (ms)   │ Count   │ Avg (ms)     │ Avg (µs)     │
├────────────────────────────────────────────────────────────────┼──────────────┼─────────┼──────────────┼──────────────┤
│ CursorRenderer.UpdateBlinkState                                │         0.06 │      60 │        0.001 │         0.92 │
│ TerminalController.Render                                      │       606.93 │      60 │       10.115 │     10115.50 │
│   <self>                                                       │         3.28 │      60 │        0.055 │        54.72 │
│   TerminalUiFonts.EnsureFontsLoaded                            │         0.01 │      60 │        0.000 │         0.11 │
│   RenderTerminalCanvas                                         │       603.64 │      60 │       10.061 │     10060.67 │
│     <self>                                                     │         0.02 │      60 │        0.000 │         0.34 │
│     RenderTerminalContent                                      │       603.62 │      60 │       10.060 │     10060.32 │
│       <self>                                                   │         1.87 │      60 │        0.031 │        31.20 │
│       Font.Push                                                │         0.04 │      60 │        0.001 │         0.70 │
│       GetViewportRows                                          │         0.25 │      60 │        0.004 │         4.11 │
│       CellRenderingLoop                                        │       601.11 │      60 │       10.019 │     10018.51 │
│         <self>                                                 │        30.28 │      60 │        0.505 │       504.68 │
│         RenderCell                                             │       570.48 │  618840 │        0.001 │         0.92 │
│           <self>                                               │       178.92 │  618840 │        0.000 │         0.29 │
│           RenderCell.Setup                                     │        91.66 │  618840 │        0.000 │         0.15 │
│             <self>                                             │        59.96 │  618840 │        0.000 │         0.10 │
│             RenderCell.SelectionCheck                          │        31.70 │  618840 │        0.000 │         0.05 │
│           RenderCell.ResolveColors                             │        44.49 │  618840 │        0.000 │         0.07 │
│           StyleManager.ApplyAttributes                         │        33.93 │  618840 │        0.000 │         0.05 │
│           RenderCell.ApplyOpacity                              │        53.65 │  618840 │        0.000 │         0.09 │
│           RenderCell.RunBatching                               │       136.33 │  305283 │        0.000 │         0.45 │
│             <self>                                             │        56.40 │  305283 │        0.000 │         0.18 │
│             Font.SelectAndRender.SelectFont                    │        43.64 │  305283 │        0.000 │         0.14 │
│               <self>                                           │        29.08 │  305283 │        0.000 │         0.10 │
│               TerminalUiFonts.SelectFont                       │        14.56 │  305283 │        0.000 │         0.05 │
│             RenderCell.ConvertFgToU32                          │        14.91 │  305283 │        0.000 │         0.05 │
│             RenderCell.RunBatching.MergeDecision               │        14.77 │  260733 │        0.000 │         0.06 │
│             RenderCell.FlushRun                                │         6.62 │   10579 │        0.001 │         0.63 │
│               <self>                                           │         1.52 │   10579 │        0.000 │         0.14 │
│               Font.SelectAndRender                             │         4.33 │   10579 │        0.000 │         0.41 │
│                 <self>                                         │         2.05 │   10579 │        0.000 │         0.19 │
│                 Font.SelectAndRender.PushFont                  │         0.64 │   10579 │        0.000 │         0.06 │
│                 Font.SelectAndRender.AddText                   │         1.07 │   10579 │        0.000 │         0.10 │
│                 Font.SelectAndRender.PopFont                   │         0.57 │   10579 │        0.000 │         0.05 │
│               RenderCell.FlushRun.DecorationsLoop              │         0.77 │   10579 │        0.000 │         0.07 │
│           RenderCell.FlushRun                                  │        29.48 │   44155 │        0.001 │         0.67 │
│             <self>                                             │         6.21 │   44155 │        0.000 │         0.14 │
│             Font.SelectAndRender                               │        18.45 │   44155 │        0.000 │         0.42 │
│               <self>                                           │         8.24 │   44155 │        0.000 │         0.19 │
│               Font.SelectAndRender.PushFont                    │         2.60 │   44155 │        0.000 │         0.06 │
│               Font.SelectAndRender.AddText                     │         5.28 │   44155 │        0.000 │         0.12 │
│               Font.SelectAndRender.PopFont                     │         2.32 │   44155 │        0.000 │         0.05 │
│             RenderCell.FlushRun.DecorationsLoop                │         4.82 │   44155 │        0.000 │         0.11 │
│           RenderCell.DrawBackground                            │         2.00 │   33120 │        0.000 │         0.06 │
│         RenderCell.FlushRun                                    │         0.35 │     395 │        0.001 │         0.88 │
│           <self>                                               │         0.05 │     395 │        0.000 │         0.13 │
│           Font.SelectAndRender                                 │         0.20 │     395 │        0.001 │         0.52 │
│             <self>                                             │         0.08 │     395 │        0.000 │         0.20 │
│             Font.SelectAndRender.PushFont                      │         0.03 │     395 │        0.000 │         0.07 │
│             Font.SelectAndRender.AddText                       │         0.08 │     395 │        0.000 │         0.21 │
│             Font.SelectAndRender.PopFont                       │         0.02 │     395 │        0.000 │         0.05 │
│           RenderCell.FlushRun.DecorationsLoop                  │         0.09 │     395 │        0.000 │         0.23 │
│       RenderCursor                                             │         0.14 │      60 │        0.002 │         2.33 │
│         <self>                                                 │         0.14 │      60 │        0.002 │         2.27 │
│         CursorRenderer.RenderCursor                            │         0.00 │      60 │        0.000 │         0.05 │
│       HandleMouseInput                                         │         0.21 │      60 │        0.003 │         3.47 │
└────────────────────────────────────────────────────────────────┴──────────────┴─────────┴──────────────┴──────────────┘