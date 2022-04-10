using System.Text.Json.Serialization;

namespace AzureBot.Discord;

public record ApplicationCommandOption
{
    public ApplicationCommandOption(string name, ApplicationCommandOptionType type)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
    }

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("type")]
    public ApplicationCommandOptionType Type { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("options")]
    public IReadOnlyCollection<ApplicationCommandOption>? Options { get; init; }

    [JsonPropertyName("required")]
    public bool? Required { get; init; }
}
