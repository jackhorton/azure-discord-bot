using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureBot.Discord;

public record ApplicationCommandOption
{
    public ApplicationCommandOption(string name, ApplicationCommandOptionType type, string value, IReadOnlyCollection<ApplicationCommandOption> options)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("type")]
    public ApplicationCommandOptionType Type { get; init; }

    [JsonPropertyName("value")]
    public string Value { get; init; }

    [JsonPropertyName("options")]
    public IReadOnlyCollection<ApplicationCommandOption> Options { get; init; }
}
