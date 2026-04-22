* notes on texture usage: https://discord.com/channels/1439383096813158702/1439647450687340544/1445601094783860796
* project which is using imgui for a stationeers imgui editor https://discord.com/channels/1260011486735241329/1260011487905189897/1451715693925109942

    https://github.com/aproposmath/StationeersIC10Editor

* from tom_is_unlucky a sample gist using ksa brutal imgui
    https://discord.com/channels/1260011486735241329/1260011487905189897/1451820628716814437
    https://gist.github.com/tsholmes/a30decf870a4cc25b9c50c73f1b72ab9

# key code bits that might be helpful

```csharp
ImGui.GetStyle().ScaleAllSizes(dpiScale);
```

```csharp
io.FontGlobalScale // can't figure out where this is in BRUTAL ImGui
```

```csharp
Console.WriteLine($"Window size: {window.Width}x{window.Height}");
Console.WriteLine($"Framebuffer size: {window.FramebufferSize.X}x{window.FramebufferSize.Y}");
```

```csharp
var dpiScale = window.ContentScale[0];
```