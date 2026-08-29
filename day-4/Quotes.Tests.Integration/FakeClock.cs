using QuotesApi.Services;

namespace Quotes.Tests.Integration;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }

    public FakeClock(DateTimeOffset fixedTime)
    {
        UtcNow = fixedTime;
    }
}