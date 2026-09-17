using QuoteHub.Moderation.Domain;

namespace QuoteHub.Moderation.Tests;

// No database, no HTTP - pure aggregate behavior, mirroring
// QuoteHub.Curation.Tests/CollectionTests.cs. ModerationCase had zero
// test coverage before this - a real gap, independent of any endpoint
// work, since QuoteReportedHandler already relies on Create's behavior.
public class ModerationCaseTests
{
    private static readonly DateTimeOffset ReportedAt = DateTimeOffset.UtcNow;

    private static ModerationCase CreateValid(string reason = "Contains spam.") =>
        ModerationCase.Create(quoteId: 1, reportedByUserId: 1, reason, ReportedAt).Value;

    [Fact]
    public void Create_with_valid_reason_succeeds()
    {
        var result = ModerationCase.Create(quoteId: 1, reportedByUserId: 1, "Contains spam.", ReportedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.QuoteId);
        Assert.Equal(1, result.Value.ReportedByUserId);
        Assert.Equal("Contains spam.", result.Value.Reason);
        Assert.Equal(ReportedAt, result.Value.ReportedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_with_missing_reason_fails(string? reason)
    {
        var result = ModerationCase.Create(quoteId: 1, reportedByUserId: 1, reason!, ReportedAt);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_starts_pending_with_no_outcome()
    {
        var moderationCase = CreateValid();

        Assert.Equal(ModerationCaseStatus.Pending, moderationCase.Status);
        Assert.Null(moderationCase.Outcome);
        Assert.Null(moderationCase.DecidedAt);
    }

    [Fact]
    public void ModerationCase_has_no_public_constructor()
    {
        // Construction only via ModerationCase.Create() - a public
        // constructor here would let callers bypass the reason invariant.
        var constructors = typeof(ModerationCase).GetConstructors();

        Assert.Empty(constructors);
    }

    [Fact]
    public void Decide_hidden_succeeds_and_sets_outcome_status_and_decidedAt()
    {
        var moderationCase = CreateValid();
        var decidedAt = ReportedAt.AddMinutes(5);

        var result = moderationCase.Decide(ModerationOutcome.Hidden, decidedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(ModerationCaseStatus.Decided, moderationCase.Status);
        Assert.Equal(ModerationOutcome.Hidden, moderationCase.Outcome);
        Assert.Equal(decidedAt, moderationCase.DecidedAt);
    }

    [Fact]
    public void Decide_restored_succeeds_and_sets_outcome()
    {
        var moderationCase = CreateValid();

        var result = moderationCase.Decide(ModerationOutcome.Restored, ReportedAt.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(ModerationOutcome.Restored, moderationCase.Outcome);
    }

    [Fact]
    public void Decide_twice_fails_and_keeps_the_first_decision()
    {
        var moderationCase = CreateValid();
        var firstDecidedAt = ReportedAt.AddMinutes(5);
        moderationCase.Decide(ModerationOutcome.Hidden, firstDecidedAt);

        var result = moderationCase.Decide(ModerationOutcome.Restored, ReportedAt.AddMinutes(10));

        Assert.True(result.IsFailure);
        // A case decides once - a second call must not overwrite the
        // first decision, not even to a different outcome.
        Assert.Equal(ModerationOutcome.Hidden, moderationCase.Outcome);
        Assert.Equal(firstDecidedAt, moderationCase.DecidedAt);
    }
}
