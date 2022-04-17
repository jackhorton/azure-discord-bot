using System.ComponentModel.DataAnnotations;

namespace AzureBot.Bot.Configuration;

public record AzureBotOptions
{
    [Required]
    public string ClientId { get; init; } = default!;

    [Required]
    public string TenantId { get; init; } = default!;

    [Required]
    public string QueueUrl { get; init; } = default!;

    [Required]
    public string CosmosUrl { get; init; } = default!;

    [Required]
    public string AppPublicKey { get; init; } = default!;
}
