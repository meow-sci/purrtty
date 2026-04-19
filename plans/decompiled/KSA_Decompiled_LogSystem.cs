// Decompiled with JetBrains decompiler
// Type: Brutal.Logging.LogSystem
// Assembly: Brutal.Core.Logging, Version=2025.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 33EF34B0-1BDC-4BCB-9707-41B8CD254EDD
// Assembly location: C:\Program Files\Kitten Space Agency\Brutal.Core.Logging.dll

using Microsoft.Extensions.Logging;
using System;

#nullable enable
namespace Brutal.Logging;

public static class LogSystem
{
  internal static ILoggerFactory? Factory;
  public static bool IsEnabled;

  public static void Initialize(Action<ILoggingBuilder> builder)
  {
    LogSystem.IsEnabled = true;
    LogSystem.Factory = LoggerFactory.Create(builder);
    foreach ((string str, LogCategory logCategory) in LogCategory.Categories)
      logCategory.Logger = LogSystem.Factory.CreateLogger(str);
  }
}
