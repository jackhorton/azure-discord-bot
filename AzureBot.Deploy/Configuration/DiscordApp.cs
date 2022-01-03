using System;
using System.ComponentModel.DataAnnotations;

namespace AzureBot.Deploy.Configuration;

public record DiscordApp
{
    [Required]
    public string ApplicationId { get; init; } = default!;
    [Required]
    public Uri BotTokenVault { get; init; } = default!;
    [Required]
    public string BotTokenSecretName { get; init; } = default!;
}
