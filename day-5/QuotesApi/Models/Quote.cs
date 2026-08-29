namespace QuotesApi.Models;

public class Quote
{
    public int Id { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? CreatedByUserId { get; set; }

    // Denormalized display value (email/username at creation time) so the
    // UI can show who created a quote without joining against Users on
    // every read - CreatedByUserId alone is an opaque id.
    public string? CreatedBy { get; set; }
}