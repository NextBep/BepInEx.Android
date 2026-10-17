using BepInEx.Configuration;
using BepInEx.Logging;

namespace BepInEx.Unity.IL2CPP.Logging;

/// <summary>
/// Android logging utility.
/// </summary>
public class AndroidLogListener : ILogListener
{
    protected static readonly ConfigEntry<LogLevel> ConfigConsoleDisplayedLevel = ConfigFile.CoreConfig.Bind(
     "Logging.Console", "LogLevels",
     LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info,
     "Only displays the specified log levels in the console output.");

    public LogLevel LogLevelFilter => ConfigConsoleDisplayedLevel.Value;

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        var logLevel = eventArgs.Level switch
        {
            LogLevel.None => AndroidLogLevel.Unknown,
            LogLevel.Fatal => AndroidLogLevel.Fatal,
            LogLevel.Error => AndroidLogLevel.Error,
            LogLevel.Warning => AndroidLogLevel.Warn,
            LogLevel.Message => AndroidLogLevel.Verbose,
            LogLevel.Info => AndroidLogLevel.Info,
            LogLevel.Debug => AndroidLogLevel.Debug,
            var _ => AndroidLogLevel.Default
        };

        NextInterop.write_log_level((int)logLevel, eventArgs.ToStringLine());
    }

    public void Dispose() { }

    enum AndroidLogLevel
    {
        Unknown = 0,
        Default = 1,
        Verbose = 2,
        Debug = 3,
        Info = 4,
        Warn = 5,
        Error = 6,
        Fatal = 7,
        Silent = 8
    }
}
