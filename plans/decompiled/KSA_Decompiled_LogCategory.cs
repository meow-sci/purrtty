// Decompiled with JetBrains decompiler
// Type: Brutal.Logging.LogCategory
// Assembly: Brutal.Core.Logging, Version=2025.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 33EF34B0-1BDC-4BCB-9707-41B8CD254EDD
// Assembly location: C:\Program Files\Kitten Space Agency\Brutal.Core.Logging.dll

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#nullable enable
namespace Brutal.Logging;

public sealed class LogCategory
{
  internal static readonly Dictionary<string, LogCategory> Categories = new Dictionary<string, LogCategory>();
  public bool IsCategoryEnabled = true;
  public bool AppendCallerInfo;

  public static LogCategory Get(string categoryName) => LogCategory.Categories[categoryName];

  public static bool TryGet(string categoryName, [MaybeNullWhen(false)] out LogCategory category)
  {
    return LogCategory.Categories.TryGetValue(categoryName, out category);
  }

  public static LogCategory Make(string categoryName)
  {
    LogCategory logCategory1;
    if (LogCategory.Categories.TryGetValue(categoryName, out logCategory1))
      return logCategory1;
    LogCategory logCategory2 = new LogCategory(categoryName)
    {
      Logger = LogSystem.Factory?.CreateLogger(categoryName)
    };
    LogCategory.Categories[categoryName] = logCategory2;
    return logCategory2;
  }

  public string Name { get; }

  public ILogger? Logger { get; internal set; }

  private LogCategory(string name) => this.Name = name;

  private bool LoggingEnabled => LogSystem.IsEnabled && this.IsCategoryEnabled;

  public void Debug(
    string message,
    [CallerMemberName] string sourceMemberName = "",
    [CallerFilePath] string sourceFilePath = "",
    [CallerLineNumber] int sourceLineNumber = 0)
  {
    if (!this.LoggingEnabled)
      return;
    if (this.Logger == null)
      throw new LogNotInitializedException();
    if (this.AppendCallerInfo)
      this.Logger.LogDebug("{Message} {SourceMemberName} (in {SourceFilePath}:{SourceLineNumber})", (object) message, (object) sourceMemberName, (object) sourceFilePath, (object) sourceLineNumber);
    else
      this.Logger.LogDebug("{Message}", (object) message);
  }

  public void Trace(
    string message = "",
    [CallerMemberName] string sourceMemberName = "",
    [CallerFilePath] string sourceFilePath = "",
    [CallerLineNumber] int sourceLineNumber = 0)
  {
    if (!this.LoggingEnabled)
      return;
    if (this.Logger == null)
      throw new LogNotInitializedException();
    if (this.AppendCallerInfo)
      this.Logger.LogTrace("{Message} {SourceMemberName} (in {SourceFilePath}:{SourceLineNumber})", (object) message, (object) sourceMemberName, (object) sourceFilePath, (object) sourceLineNumber);
    else
      this.Logger.LogTrace("{Message}", (object) message);
  }

  public void Info(
    string message,
    [CallerMemberName] string sourceMemberName = "",
    [CallerFilePath] string sourceFilePath = "",
    [CallerLineNumber] int sourceLineNumber = 0)
  {
    if (!this.LoggingEnabled)
      return;
    if (this.Logger == null)
      throw new LogNotInitializedException();
    if (this.AppendCallerInfo)
      this.Logger.LogInformation("{Message} {SourceMemberName} (in {SourceFilePath}:{SourceLineNumber})", (object) message, (object) sourceMemberName, (object) sourceFilePath, (object) sourceLineNumber);
    else
      this.Logger.LogInformation("{Message}", (object) message);
  }

  public void Warning(
    string message,
    [CallerMemberName] string sourceMemberName = "",
    [CallerFilePath] string sourceFilePath = "",
    [CallerLineNumber] int sourceLineNumber = 0)
  {
    if (!this.LoggingEnabled)
      return;
    if (this.Logger == null)
      throw new LogNotInitializedException();
    if (this.AppendCallerInfo)
      this.Logger.LogWarning("{Message} {SourceMemberName} (in {SourceFilePath}:{SourceLineNumber})", (object) message, (object) sourceMemberName, (object) sourceFilePath, (object) sourceLineNumber);
    else
      this.Logger.LogWarning("{Message}", (object) message);
  }

  public void Error(
    string message,
    [CallerMemberName] string sourceMemberName = "",
    [CallerFilePath] string sourceFilePath = "",
    [CallerLineNumber] int sourceLineNumber = 0)
  {
    if (!this.LoggingEnabled)
      return;
    if (this.Logger == null)
      throw new LogNotInitializedException();
    if (this.AppendCallerInfo)
      this.Logger.LogError("{Message} {SourceMemberName} (in {SourceFilePath}:{SourceLineNumber})", (object) message, (object) sourceMemberName, (object) sourceFilePath, (object) sourceLineNumber);
    else
      this.Logger.LogError("{Message}", (object) message);
  }

  public void Critical(
    string message,
    [CallerMemberName] string sourceMemberName = "",
    [CallerFilePath] string sourceFilePath = "",
    [CallerLineNumber] int sourceLineNumber = 0)
  {
    if (!this.LoggingEnabled)
      return;
    if (this.Logger == null)
      throw new LogNotInitializedException();
    if (this.AppendCallerInfo)
      this.Logger.LogCritical("{Message} {SourceMemberName} (in {SourceFilePath}:{SourceLineNumber})", (object) message, (object) sourceMemberName, (object) sourceFilePath, (object) sourceLineNumber);
    else
      this.Logger.LogCritical("{Message}", (object) message);
  }

  public void Error(
    Exception exception,
    [CallerMemberName] string sourceMemberName = "",
    [CallerFilePath] string sourceFilePath = "",
    [CallerLineNumber] int sourceLineNumber = 0)
  {
    if (!this.LoggingEnabled)
      return;
    if (this.Logger == null)
      throw new LogNotInitializedException();
    this.Logger.LogError("{Exception} {SourceMemberName} (in {SourceFilePath}:{SourceLineNumber})", (object) exception.ToString(), (object) sourceMemberName, (object) sourceFilePath, (object) sourceLineNumber);
  }

  public void Critical(
    Exception exception,
    [CallerMemberName] string sourceMemberName = "",
    [CallerFilePath] string sourceFilePath = "",
    [CallerLineNumber] int sourceLineNumber = 0)
  {
    if (!this.LoggingEnabled)
      return;
    if (this.Logger == null)
      throw new LogNotInitializedException();
    this.Logger.LogCritical("{Exception} {SourceMemberName} (in {SourceFilePath}:{SourceLineNumber})", (object) exception.ToString(), (object) sourceMemberName, (object) sourceFilePath, (object) sourceLineNumber);
  }
}
