using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuotesApi.Diagnostics;

// Counts every SQL command EF Core actually sends to SQLite - a real
// database round trip, not an assumption about how many the code above
// it *should* cause. Registered as a singleton and handed to every
// QuotesDbContext instance via AddInterceptors (see
// InfrastructureExtensions.cs), so the count accumulates process-wide
// across every request/scope, not per-DbContext.
//
// Day 11 counted queries via OpenTelemetry spans exported to Application
// Insights and a KQL query over them (see day-11/QuotesApi/KQL.md on the
// day11-profile branch) - useful for finding N+1s after the fact, but not
// something a load-test harness can read synchronously mid-run. This is
// a small, purpose-built in-process counter instead, exposed via
// GET /api/diagnostics/db-hits (Program.cs) for exactly that.
public class DbHitCounterInterceptor : DbCommandInterceptor
{
    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
