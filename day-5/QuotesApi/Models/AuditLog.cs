namespace QuotesApi.Models;

// Written by AuditLogWorker (BackgroundJobs/), never directly by a
// request handler - see day-18/README.md for why this is a background
// job instead of an inline write.
public class AuditLog
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
