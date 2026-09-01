using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Configuration;

public record ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    public string TopicName { get; init; } = string.Empty;
}
