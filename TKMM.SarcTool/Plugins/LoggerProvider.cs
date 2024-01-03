using Microsoft.Extensions.Logging;
using Spectre.Console;
using TKMM.SarcTool.Common;

namespace TKMM.SarcTool;

public sealed class SpectreConsoleLogger : ILogger {

    private readonly IGlobals globals;

    public SpectreConsoleLogger(IGlobals globals) {
        this.globals = globals;
    }
    
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {

        if (logLevel is LogLevel.Critical or LogLevel.Error) {
            AnsiConsole.MarkupLineInterpolated($"X [red]{formatter(state, null)}[/]");
        } else if (logLevel is LogLevel.Warning) {
            AnsiConsole.MarkupLineInterpolated($"! [orange]{formatter(state, null)}[/]");
        } else if (logLevel is LogLevel.Information or LogLevel.None) {
            AnsiConsole.MarkupLineInterpolated($"- {formatter(state, null)}");
        } else if (logLevel is LogLevel.Trace or LogLevel.Debug) {
            if (globals.Verbose) {
                AnsiConsole.MarkupLineInterpolated($"- [grey19]{formatter(state, null)}[/]");
            }
        }

        if (exception != null)
            AnsiConsole.WriteException(exception, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
        
    }

    public bool IsEnabled(LogLevel logLevel) {
        if (logLevel is LogLevel.Trace or LogLevel.Debug) {
            return globals.Verbose;
        }

        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
        return null;
    }
}