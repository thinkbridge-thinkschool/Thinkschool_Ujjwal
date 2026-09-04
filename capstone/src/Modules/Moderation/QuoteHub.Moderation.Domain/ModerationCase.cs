using QuoteHub.SharedKernel;

namespace QuoteHub.Moderation.Domain;

// Moderation's own aggregate: one open case per report, opened from a
// QuoteReported integration event and closed by a moderator's decision.
// Kept deliberately small - Collection is this capstone's core aggregate;
// this exists so Moderation is a real module with a real invariant
// (a case decides once), not an empty shell the architecture tests would
// have nothing to say about.
public sealed class ModerationCase : AggregateRoot<int>
{
    public int QuoteId { get; private set; }
    public int ReportedByUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset ReportedAt { get; private set; }
    public ModerationCaseStatus Status { get; private set; }
    public ModerationOutcome? Outcome { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    private ModerationCase() { } // EF

    private ModerationCase(int quoteId, int reportedByUserId, string reason, DateTimeOffset reportedAt)
    {
        QuoteId = quoteId;
        ReportedByUserId = reportedByUserId;
        Reason = reason;
        ReportedAt = reportedAt;
        Status = ModerationCaseStatus.Pending;
    }

    public static Result<ModerationCase> Create(int quoteId, int reportedByUserId, string reason, DateTimeOffset reportedAt)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result<ModerationCase>.Failure("A report must include a reason.");

        return Result<ModerationCase>.Success(new ModerationCase(quoteId, reportedByUserId, reason, reportedAt));
    }

    // A case decides once. A quote reported again after this case is
    // decided opens a new case rather than reopening this one - keeps
    // "when was this decided" unambiguous.
    public Result Decide(ModerationOutcome outcome, DateTimeOffset decidedAt)
    {
        if (Status == ModerationCaseStatus.Decided)
            return Result.Failure("This case has already been decided.");

        Outcome = outcome;
        Status = ModerationCaseStatus.Decided;
        DecidedAt = decidedAt;
        return Result.Success();
    }
}
