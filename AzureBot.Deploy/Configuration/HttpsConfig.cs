using System;
using System.ComponentModel.DataAnnotations;

namespace AzureBot.Deploy.Configuration;

public record HttpsConfig
{
    [Required]
    public string Email { get; init; } = default!;
    [Required]
    public Uri Directory { get; init; } = default!;
}
