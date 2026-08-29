using FluentAssertions;
using QuotesApi.Services;

namespace Quotes.Tests.Unit.Services;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcTime()
    {
        var clock = new SystemClock();

        var before = DateTimeOffset.UtcNow;
        var result = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        result.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}
