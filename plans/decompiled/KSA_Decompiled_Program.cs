namespace KSA;
public class Program : App, IGlobalRenderSystem
{
  public static ConsoleWindow ConsoleWindow;
  public static TerminalInterface TerminalInterface => Program.ConsoleWindow.Terminal;
  private unsafe Program(IReadOnlyList<string> inArgs)
    : base(inArgs)
  {
    Program.Instance = this;
    Program.ConsoleWindow = new ConsoleWindow();
    Program.ConsoleWindow.DisableMessageFading();
    LogSystem.Initialize((Action<ILoggingBuilder>) (builder =>
    {
      builder.AddFilter("Monitor", LogLevel.Warning);
      builder.AddFilter<MonitorLoggerProvider>("Monitor", LogLevel.Debug);
      builder.AddConsole((Action<ConsoleLoggerOptions>) (options => options.FormatterName = "KSAFormatter")).AddConsoleFormatter<KSALogFormatter, ConsoleFormatterOptions>();
      builder.AddProvider((ILoggerProvider) Program.ConsoleWindow);
      builder.AddProvider((ILoggerProvider) this._brutalMonitor.LogProvider);
      builder.SetMinimumLevel(LogLevel.Debug);
    }));
    Program.TerminalInterface.Execute((ReadOnlySpan<string>) GameSettings.Current.Console.OnBoot.AsSpan<string>());
    Program.ConsoleWindow.FontPtr = FontManager.GetFontPtr(FontNames.BrutalDefault);
    Program.TerminalInterface.OnOutput += (Action<string, TerminalInterfaceOutputType>) ((s, type) => Program.ConsoleWindow.Print($"{type}: {s}"));
    GameSettings.ApplyTo(Program.ConsoleWindow);
    using (Loading.Task("Commands"))
    {
      this.RegisterCommands();
      TerminalCommands.RegisterCommands(this);
    }

    Program.TerminalInterface.Execute((ReadOnlySpan<string>) ModLibrary.OnBoot.AsSpan<string>());
    
    Program.ConsoleWindow.EnableMessageFading();
  }
  private void RegisterCommands()
  {
    Program.TerminalInterface.RegisterCommand((Delegate) (Program.\u003C\u003EO.\u003C2\u003E__SetScreenSize ?? (Program.\u003C\u003EO.\u003C2\u003E__SetScreenSize = new Action<int, int>(Program.SetScreenSize))), (object) this);
    Program.TerminalInterface.RegisterCommand((Delegate) (Program.\u003C\u003EO.\u003C3\u003E__PrintSystemInfo ?? (Program.\u003C\u003EO.\u003C3\u003E__PrintSystemInfo = new System.Action(Program.PrintSystemInfo))), (object) this);
    Program.TerminalInterface.RegisterCommand((Delegate) (Program.\u003C\u003EO.\u003C4\u003E__SetAtmosphereEditor ?? (Program.\u003C\u003EO.\u003C4\u003E__SetAtmosphereEditor = new System.Action(Program.SetAtmosphereEditor))), (object) this);
    Program.TerminalInterface.RegisterCommand((Delegate) (Program.\u003C\u003EO.\u003C5\u003E__SetCloudsEditor ?? (Program.\u003C\u003EO.\u003C5\u003E__SetCloudsEditor = new System.Action(Program.SetCloudsEditor))), (object) this);
    Program.TerminalInterface.RegisterCommand((Delegate) (Program.\u003C\u003EO.\u003C6\u003E__SetOceanEditor ?? (Program.\u003C\u003EO.\u003C6\u003E__SetOceanEditor = new System.Action(Program.SetOceanEditor))), (object) this);
    Program.TerminalInterface.RegisterCommand((Delegate) (Program.\u003C\u003EO.\u003C7\u003E__SetExhaustTestUi ?? (Program.\u003C\u003EO.\u003C7\u003E__SetExhaustTestUi = new System.Action(Program.SetExhaustTestUi))), (object) this);
    Program.TerminalInterface.RegisterCommand((Delegate) new System.Action(this.Exit), (object) this);
  }
  [TerminalAction("atmosphere", "opens the atmosphere editor", ArgParseMode.Default)]
  public static void SetAtmosphereEditor()
  {
    AtmosphereRenderer.ShowEditor = true;
    DefaultCategory.Log.Debug("Atmosphere editor opened", nameof (SetAtmosphereEditor), "C:\\prototype-planet\\KSA\\Program.cs", 993);
  }
  [TerminalAction("clouds", "opens the clouds editor", ArgParseMode.Default)]
  public static void SetCloudsEditor()
  {
    CloudRenderer.ShowEditor = true;
    DefaultCategory.Log.Debug("Clouds editor opened", nameof (SetCloudsEditor), "C:\\prototype-planet\\KSA\\Program.cs", 1000);
  }
  [TerminalAction("ocean", "opens the ocean editor", ArgParseMode.Default)]
  public static void SetOceanEditor()
  {
    OceanRenderer.ShowEditor = true;
    DefaultCategory.Log.Debug("Ocean editor opened", nameof (SetOceanEditor), "C:\\prototype-planet\\KSA\\Program.cs", 1007);
  }
  [TerminalAction("exhaust", "opens the volumetric exhausts test UI", ArgParseMode.Default)]
  public static void SetExhaustTestUi()
  {
    VolumetricExhaustRenderer.ShowEditor = true;
    DefaultCategory.Log.Debug("VolumetricExhaustsTest UI opened", nameof (SetExhaustTestUi), "C:\\prototype-planet\\KSA\\Program.cs", 1014);
  }
  [TerminalAction("systeminfo", "logs all system info", ArgParseMode.Default)]
  public static void PrintSystemInfo()
  {
    DefaultCategory.Log.Debug("logging system info...", nameof (PrintSystemInfo), "C:\\prototype-planet\\KSA\\Program.cs", 1020);
    TableString tableString = new TableString();
    tableString.AddRow("device name", Environment.MachineName);
    tableString.AddRow("processor count", Environment.ProcessorCount.ToString());
    tableString.AddRow("culture locale", CultureInfo.CurrentCulture.Name);
    tableString.AddRow("os platform", Environment.OSVersion.Platform.ToString());
    tableString.AddRow("os version", Environment.OSVersion.VersionString);
    tableString.AddRow("current directory", Environment.CurrentDirectory);
    tableString.AddRow("total vram", $"{Program.TotalVideoMemory}");
    tableString.ToConsole();
  }
  [TerminalAction("screen", "set the screen size", ArgParseMode.Default)]
  private static void SetScreenSize(int width, int height)
  {
    GameSettings.Current.UpdateScreenSize(width, height);
    GameSettings.Current.ApplyTo(Program._window);
    GameSettings.Current.Save();
  }
  [TerminalAction("exit", "close the application", ArgParseMode.Default)]
  private void Exit() => this.RequestExit(0);
  
}
