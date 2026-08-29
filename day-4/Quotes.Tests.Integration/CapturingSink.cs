using Serilog.Core;
using Serilog.Events;

namespace Quotes.Tests.Integration;

// Minimal in-memory ILogEventSink so a test can inspect what Serilog actually emitted,
// without pulling in the Serilog.Sinks.InMemory package for a single assertion.
public class CapturingSink : ILogEventSink
{
    private readonly List<LogEvent> _events = new();
    private readonly object _gate = new();

    public IReadOnlyList<LogEvent> Events
    {
        get { lock (_gate) { return _events.ToList(); } }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_gate) { _events.Add(logEvent); }
    }
}
