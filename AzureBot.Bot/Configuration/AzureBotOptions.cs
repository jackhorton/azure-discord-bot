using System.ComponentModel.DataAnnotations;

namespace AzureBot.Bot.Configuration;

public record AzureBotOptions
{
    [Required]
    public string ClientId { get; init; }

    [Required]
    public string TenantId { get; init; }

    [Required]
    public string QueueUrl { get; init; }

    [Required]
    public string CosmosUrl { get; init; }
}
