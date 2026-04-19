// Decompiled with JetBrains decompiler
// Type: Brutal.ImGuiApi.Abstractions.ConsoleWindow
// Assembly: Brutal.ImGui.Abstractions, Version=2025.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: F69EA564-B689-4F37-8535-47009C27A81C
// Assembly location: C:\Program Files\Kitten Space Agency\Brutal.ImGui.Abstractions.dll

using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.CompilerServices;

#nullable enable
namespace Brutal.ImGuiApi.Abstractions;

public class ConsoleWindow : ILoggerProvider, IDisposable
{
  private const int DEFAULT_BUFFER_SIZE = 256 /*0x0100*/;
  private const int DEFAULT_LOG_TIME = 5;
  public TerminalInterface Terminal;
  private bool _show;
  private bool _setFocus;
  private bool _overwrite;
  private bool _scrollToBottom;
  private bool _autoScroll;
  private bool _fadeMessages = true;
  private readonly ImInputString _consoleInput = new ImInputString(512 /*0x0200*/);
  private readonly ConsoleLineGui[] _consoleBuffer = new ConsoleLineGui[256 /*0x0100*/];
  private bool _clearConsoleInput;
  private float2 _inputSize;
  private bool _inputLastActive;
  private readonly string[] _commandBuffer = new string[8];
  private int _commandBufferIndex;
  private int _headIndex;
  private int _tailIndex;
  private static readonly uint DefaultColor = ConsoleColor.ColorConvertFloat4ToU32(new float4(0.7f, 0.7f, 0.7f, 1f));
  public static uint InfoColor = ConsoleWindow.DefaultColor;
  public static uint DebugColor = ConsoleColor.Integer.Yellow;
  public static uint WarningColor = ConsoleColor.Integer.Orange;
  public static uint ErrorColor = ConsoleColor.Integer.Red;
  public static uint CriticalColor = ConsoleColor.Integer.Red;
  public static uint TraceColor = ConsoleColor.Integer.GreenGrey;
  public float FontScale = 1f;
  public ImFontPtr FontPtr;

  public bool IsOpen => this._show;

  public ImFontPtr GetDefaultFont()
  {
    ImFontAtlasPtr target = Brutal.ImGuiApi.ImGui.GetIO().Fonts;
    ImVector<ImFontPtr> imVector = target.Fonts;
    return imVector.Count != 0 ? imVector[0] : target.AddFontDefault();
  }

  public ILogger CreateLogger(string categoryName)
  {
    return (ILogger) new ConsoleWindowLogger(this, categoryName);
  }

  public void OnLog(string categoryName, LogLevel logLevel, string message)
  {
    switch (logLevel)
    {
      case LogLevel.Trace:
        this.Print(message, ConsoleWindow.TraceColor);
        break;
      case LogLevel.Debug:
        this.Print(message, ConsoleWindow.DebugColor);
        break;
      case LogLevel.Information:
      case LogLevel.None:
        this.Print(message, ConsoleWindow.InfoColor);
        break;
      case LogLevel.Warning:
        this.Print(message, ConsoleWindow.WarningColor);
        break;
      case LogLevel.Error:
        this.Print(message, ConsoleWindow.ErrorColor);
        break;
      case LogLevel.Critical:
        this.Print(message, ConsoleWindow.CriticalColor);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof (logLevel), (object) logLevel, (string) null);
    }
  }

  public void Print(string inOutput, int inTimeToShow = 0)
  {
    this.Print(inOutput, ConsoleWindow.DefaultColor, inTimeToShow);
  }

  public void PrintMessage(string inOutput)
  {
    this.Print(inOutput, ConsoleColor.Integer.LightBlue, inType: ConsoleLineType.Message);
  }

  public ConsoleWindow()
  {
    for (int index = 0; index < 256 /*0x0100*/; ++index)
      this._consoleBuffer[index] = new ConsoleLineGui();
    this.Terminal = new TerminalInterface(this);
  }

  public void Show()
  {
    this._show = true;
    this._setFocus = true;
    this._inputLastActive = true;
  }

  public void Hide()
  {
    this._show = false;
    this._inputLastActive = false;
  }

  public void EnableMessageFading() => this._fadeMessages = true;

  public void DisableMessageFading() => this._fadeMessages = false;

  private void DrawLog(float inUnscaledDeltaTime, bool inNoFade = false)
  {
    this._inputSize = Brutal.ImGuiApi.ImGui.GetStyle().WindowPadding;
    for (int index = this._consoleBuffer.Length - 1; index >= 0; --index)
      this._consoleBuffer[index].Draw(ref this._inputSize, this._show, inUnscaledDeltaTime, inNoFade);
  }

  public unsafe void Draw(
    float2 inViewportPosition,
    float2 inViewportSize,
    float inUnscaledDeltaTime,
    bool inNoFade = false)
  {
    // omitted..brevity
    if (Brutal.ImGuiApi.ImGui.Begin((ImString) nameof (ConsoleWindow), ref this._show, flags))
    {
      Brutal.ImGuiApi.ImGui.PopStyleColor(2);
      Brutal.ImGuiApi.ImGui.PopStyleVar();
      if (this._show)
      {
        // omitted..brevity
        Brutal.ImGuiApi.ImGui.Text((ImString) ">");
        // omitted..brevity
        
        
        if (this._overwrite)
        {
          this._overwrite = false;
          Brutal.ImGuiApi.Internal.ImGui.GetInputTextState(Brutal.ImGuiApi.ImGui.GetID((ImString) readOnlySpan)).ReloadUserBufAndMoveToEnd();
        }
        Brutal.ImGuiApi.ImGui.InputText((ImString) readOnlySpan, this._consoleInput, userData: IntPtr.Zero);
        this._inputLastActive = Brutal.ImGuiApi.ImGui.IsItemActive();
        ImGuiUtils.SetLastFocusOnAppearing(true);
        this._inputSize += Brutal.ImGuiApi.ImGui.GetItemRectSize();
        Brutal.ImGuiApi.ImGui.PopStyleColor(2);
        Brutal.ImGuiApi.ImGui.PopItemWidth();
        if (Brutal.ImGuiApi.ImGui.IsItemVisible())
        {
          // omitted..brevity
        }
      }
      else
        this.DrawLog(inUnscaledDeltaTime, inNoFade);
      if (this._clearConsoleInput)
      {
        this._clearConsoleInput = false;
        this._consoleInput.Clear();
        // ISSUE: field reference
        this._consoleInput.SetValue(RuntimeHelpers.CreateSpan<char>(__fieldref (\u003CPrivateImplementationDetails\u003E.\u00396A296D224F285C67BEE93C30F8A309157F0DAA35DC5B87E410B78630A09CFC72)));
        this._scrollToBottom = true;
      }
    }
  }

  public void Submit(string inInput)
  {
    this._consoleInput.Value16 = inInput.AsSpan();
    this.Submit();
  }

  private void Submit()
  {
    if (this._consoleInput.IsEmpty)
    {
      this.Hide();
    }
    else
    {
      this.Terminal.Execute(this._consoleInput.ToString());
      this._clearConsoleInput = true;
      this._setFocus = true;
      this._scrollToBottom = true;
    }
  }

  public void ClearConsole()
  {
    for (int index = 0; index < this._consoleBuffer.Length; ++index)
      this._consoleBuffer[index].Clear();
  }

  public void Print(string inOutput, uint inColor, int inTimeToShow = -1, ConsoleLineType inType = ConsoleLineType.Default)
  {
    if (inTimeToShow < 0)
      inTimeToShow = 5;
    if (!this._fadeMessages)
      inTimeToShow = 0;
    for (int index = this._consoleBuffer.Length - 1 - 1; index >= 0; --index)
      this._consoleBuffer[index + 1].Apply(this._consoleBuffer[index]);
    this._consoleBuffer[0].Set(inOutput, inColor, (float) inTimeToShow, inType);
  }

  public bool HandleInputAction(ConsoleWindow.Actions action)
  {
    if (this._inputLastActive)
    {
      bool flag;
      switch (action)
      {
        case ConsoleWindow.Actions.Up:
        case ConsoleWindow.Actions.Down:
          flag = true;
          break;
        default:
          flag = false;
          break;
      }
      if (flag)
      {
        if (this._commandBufferIndex == this._headIndex)
          this._commandBuffer[this._headIndex] = this._consoleInput.ToString();
        if (action == ConsoleWindow.Actions.Up)
        {
          int num = this._commandBufferIndex == 0 ? this._commandBuffer.Length - 1 : this._commandBufferIndex - 1;
          if (this._commandBufferIndex != this._tailIndex)
          {
            this._commandBufferIndex = num;
            this._consoleInput.Value16 = this._commandBuffer[this._commandBufferIndex].AsSpan();
            this._overwrite = true;
          }
        }
        else if (this._commandBufferIndex != this._headIndex)
        {
          this._commandBufferIndex = (this._commandBufferIndex + 1) % this._commandBuffer.Length;
          this._consoleInput.Value16 = this._commandBuffer[this._commandBufferIndex].AsSpan();
          this._overwrite = true;
        }
        return true;
      }
    }
    if (!this._show && action == ConsoleWindow.Actions.Toggle)
    {
      this.Show();
      return true;
    }
    if (!this._show)
      return false;
    switch (action)
    {
      case ConsoleWindow.Actions.Submit:
        this.Submit();
        if (!this._consoleInput.IsEmpty)
        {
          string str = this._consoleInput.ToString();
          if (this._commandBufferIndex == this._headIndex)
          {
            this._commandBuffer[this._commandBufferIndex] = str;
            this._headIndex = (this._commandBufferIndex + 1) % this._commandBuffer.Length;
          }
          else if (str != this._commandBuffer[this._commandBufferIndex])
          {
            this._commandBuffer[this._headIndex] = str;
            this._headIndex = (this._headIndex + 1) % this._commandBuffer.Length;
          }
          this._commandBufferIndex = this._headIndex;
          if (this._headIndex == this._tailIndex)
            this._tailIndex = (this._tailIndex + 1) % this._commandBuffer.Length;
          this._consoleInput.Clear();
          this._overwrite = true;
        }
        return true;
      case ConsoleWindow.Actions.Toggle:
        this.Hide();
        return true;
      default:
        return false;
    }
  }

  public void Dispose()
  {
  }

  public enum Actions
  {
    Up,
    Down,
    Submit,
    Toggle,
  }
}
