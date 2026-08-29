using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Configuration;

public record EntraOptions
{
    public const string SectionName = "Entra";

    [Required]
    public string TenantId { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;
}
