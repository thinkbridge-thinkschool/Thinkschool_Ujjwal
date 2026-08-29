namespace QuotesApi.Configuration;

public record ApplicationInsightsOptions
{
    public const string SectionName = "ApplicationInsights";

    // Not [Required]: absent is a legitimate state - the app must run normally
    // without it (e.g. locally, with no Application Insights resource configured).
    public string ConnectionString { get; init; } = string.Empty;
}
