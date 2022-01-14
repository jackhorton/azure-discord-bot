using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AzureBot.Deploy.Configuration;

public record DiscordAppConfig
{
    [Required]
    public string ApplicationId { get; init; } = default!;
    [Required]
    public Uri BotTokenVault { get; init; } = default!;
    [Required]
    public string BotTokenSecretName { get; init; } = default!;
    public IReadOnlyDictionary<string, string>? WellKnownGuilds { get; init; }
}
