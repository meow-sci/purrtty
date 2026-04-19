// Decompiled with JetBrains decompiler
// Type: Brutal.ImGuiApi.Abstractions.TerminalInterface
// Assembly: Brutal.ImGui.Abstractions, Version=2025.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: F69EA564-B689-4F37-8535-47009C27A81C
// Assembly location: C:\Program Files\Kitten Space Agency\Brutal.ImGui.Abstractions.dll

using Brutal.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

#nullable enable
namespace Brutal.ImGuiApi.Abstractions;

public class TerminalInterface
{
  public static LogCategory Log = LogCategory.Make(nameof (TerminalInterface));
  private readonly Dictionary<object, Dictionary<string, Command>> CommandLibrary = new Dictionary<object, Dictionary<string, Command>>();
  private readonly Dictionary<string, Command> StaticCommandLibrary = new Dictionary<string, Command>();
  private readonly ConsoleWindow _consoleWindow;

  public event Action<string, TerminalInterfaceOutputType>? OnOutput;

  public TerminalInterface(ConsoleWindow consoleWindow)
  {
    this._consoleWindow = consoleWindow;
    this.RegisterCommand((Delegate) new Action(this.ListCommands));
    this.RegisterCommand((Delegate) new Action(this.ClearConsole));
  }

  public void Execute([ParamCollection] scoped ReadOnlySpan<string> inInput)
  {
    ReadOnlySpan<string> readOnlySpan = inInput;
    for (int index = 0; index < readOnlySpan.Length; ++index)
      this.Execute(readOnlySpan[index]);
  }

  public bool Execute(string inInput)
  {
    foreach (string command in Parser.SplitToCommands(inInput))
    {
      try
      {
        this.ProcessCommand(command);
      }
      catch (TerminalInterfaceException ex)
      {
        this.OutputProcessCommandError(command, ex.Message);
        return false;
      }
    }
    return true;
  }

  private Type? GetDelegateTypeForMethod(MethodBase inMethodInfo)
  {
    ParameterInfo[] parameters = inMethodInfo.GetParameters();
    int numArgs = parameters.Length;
    if (numArgs == 0)
      return typeof (Action);
    return ((IEnumerable<Type>) typeof (Action).Assembly.GetTypes()).FirstOrDefault<Type>((Func<Type, bool>) (t => t.Name == $"Action`{numArgs}"))?.MakeGenericType(((IEnumerable<ParameterInfo>) parameters).Select<ParameterInfo, Type>((Func<ParameterInfo, Type>) (p => p.ParameterType)).ToArray<Type>());
  }

  public void RegisterObject(object inObject)
  {
    foreach (MethodInfo methodInfo in this.GetMethodInfos(inObject))
    {
      Type delegateTypeForMethod = this.GetDelegateTypeForMethod((MethodBase) methodInfo);
      if (!(delegateTypeForMethod != (Type) null))
        throw new TerminalInterfaceException($"Error registering command: callback {methodInfo.Name} has too many parameters");
      if (methodInfo.IsStatic)
        this.RegisterCommand(Delegate.CreateDelegate(delegateTypeForMethod, methodInfo), inObject);
      else
        this.RegisterCommand(Delegate.CreateDelegate(delegateTypeForMethod, inObject, methodInfo), inObject);
    }
  }

  private IEnumerable<MethodInfo> GetMethodInfos(object inObject)
  {
    return ((IEnumerable<MethodInfo>) inObject.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)).Where<MethodInfo>((Func<MethodInfo, bool>) (m => m.GetCustomAttribute<TerminalActionAttribute>() != null));
  }

  public void UnregisterObject(object inObject)
  {
    if (!this.CommandLibrary.ContainsKey(inObject))
      return;
    this.CommandLibrary.Remove(inObject);
  }

  public void RegisterCommand(Delegate inCallback, object? inObject = null)
  {
    TerminalActionAttribute customAttribute = inCallback.Method.GetCustomAttribute<TerminalActionAttribute>();
    if (customAttribute == null)
      throw new TerminalInterfaceException($"Error registering command: callback {inCallback.Method.Name} has no TerminalActionAttribute");
    if (inObject != null)
    {
      if (!this.CommandLibrary.ContainsKey(inObject))
        this.CommandLibrary[inObject] = new Dictionary<string, Command>();
      this.CommandLibrary[inObject][customAttribute.CommandName] = new Command(inCallback, customAttribute.Description, customAttribute.ParsingMode);
    }
    else
      this.StaticCommandLibrary[customAttribute.CommandName] = new Command(inCallback, customAttribute.Description, customAttribute.ParsingMode);
  }

  private void ProcessCommand(string inCommandString)
  {
    string inCommandName;
    string inArgsString;
    Parser.SplitCommand(inCommandString, out inCommandName, out inArgsString);
    if (this.StaticCommandLibrary.ContainsKey(inCommandName))
    {
      string[] args = Parser.SplitToArgs(inArgsString, this.StaticCommandLibrary[inCommandName].ParsingMode);
      this.StaticCommandLibrary[inCommandName].Issue(args);
    }
    else
    {
      foreach ((object _, Dictionary<string, Command> dictionary) in this.CommandLibrary)
      {
        if (dictionary.ContainsKey(inCommandName))
        {
          string[] args = Parser.SplitToArgs(inArgsString, dictionary[inCommandName].ParsingMode);
          dictionary[inCommandName].Issue(args);
          return;
        }
      }
      TerminalInterface.Log.Error($"unknown command {inCommandName}, use 'help' for a list of commands", nameof (ProcessCommand), "/opt/actions-runner/_work/brutal.imgui/brutal.imgui/src/Brutal.ImGui.Abstractions/ConsoleWindow/Terminal/TerminalInterface.cs", 217);
    }
  }

  [TerminalAction("clear", "Clears the console output", ArgParseMode.Default)]
  private void ClearConsole() => this._consoleWindow.ClearConsole();

  [TerminalAction("help", "Lists all registered commands and their descriptions", ArgParseMode.Default)]
  private void ListCommands()
  {
    int commandCount = 0;
    TableString tableString = new TableString();
    Dictionary<string, Command> dictionary1 = new Dictionary<string, Command>();
    foreach ((string key3, Command command3) in this.StaticCommandLibrary)
    {
      string key2 = key3;
      Command command2 = command3;
      dictionary1.Add(key2, command2);
    }
    foreach ((object _, Dictionary<string, Command> dictionary2) in this.CommandLibrary)
    {
      foreach ((key3, command3) in dictionary2)
      {
        string key4 = key3;
        Command command4 = command3;
        dictionary1.Add(key4, command4);
      }
    }
    foreach (KeyValuePair<string, Command> keyValuePair in dictionary1)
      PrintHelpLine(keyValuePair.Key, keyValuePair.Value);
    this._consoleWindow.Print($"listing {commandCount} commands");
    this._consoleWindow.Print("Multiple commands can be called in a single statement by separating them with a ';' semicolon");
    tableString.Sort();
    tableString.ToConsole(this._consoleWindow);
    Action<string, TerminalInterfaceOutputType> onOutput = this.OnOutput;
    if (onOutput == null)
      return;
    onOutput(string.Empty, TerminalInterfaceOutputType.Message);

    void PrintHelpLine(string command, Command args)
    {
      commandCount++;
      IReadOnlyList<ArgDefinition> getDefinitions = args.GetDefinitions;
      if (getDefinitions.Count == 0)
      {
        tableString.AddRow(command, args.Description);
      }
      else
      {
        StringBuilder stringBuilder = new StringBuilder();
        for (int index = 0; index < getDefinitions.Count; ++index)
        {
          if (index > 0)
            stringBuilder.Append(", ");
          ArgDefinition getDefinition = args.GetDefinitions[index];
          stringBuilder.Append('<');
          string str1 = getDefinition.Type.ToString();
          if (str1.StartsWith("System."))
          {
            string str2 = str1;
            str1 = str2.Substring(7, str2.Length - 7);
          }
          string lowerInvariant = str1.ToLowerInvariant();
          stringBuilder.Append(lowerInvariant);
          stringBuilder.Append('>');
          if (getDefinition.HasDefault)
            stringBuilder.Append('?');
          stringBuilder.Append(' ');
          stringBuilder.Append(getDefinition.Name);
        }
        tableString.AddRow(command, args.Description, stringBuilder.ToString());
      }
    }
  }

  private void OutputProcessCommandError(string inCommand, string inErrorMessage)
  {
    string str = $"Terminal interface failed to process command: {inCommand}, error: {inErrorMessage}";
    Action<string, TerminalInterfaceOutputType> onOutput = this.OnOutput;
    if (onOutput == null)
      return;
    onOutput(str, TerminalInterfaceOutputType.Error);
  }
}
