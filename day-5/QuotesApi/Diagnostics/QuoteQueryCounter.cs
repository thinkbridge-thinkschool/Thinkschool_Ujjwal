namespace QuotesApi.Diagnostics;

// Counts calls to QuoteRepository.GetPagedAsync specifically - the exact
// thing the Day 21 cache sits in front of. This exists separately from
// DbHitCounterInterceptor because that interceptor counts EVERY SQL
// command across the whole process, and this app already has background
// activity of its own: OutboxRelay (Day 20) polls the OutboxMessages
// table every 5 seconds regardless of load-test traffic, on the same
// QuotesDbContext registration, so the same interceptor instance. Found
// this by actually running the load test and seeing counts that didn't
// match "one request, one query" - not assumed in advance. For measuring
// "did GET /api/quotes reach the database," this counter is the correct,
// noise-free instrument; DbHitCounterInterceptor's total remains useful
// as a general "how much SQL is this process running" figure, but it's
// not what the cold-cache stampede numbers in day-21/README.md are taken
// from.
public class QuoteQueryCounter
{
    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);
}
