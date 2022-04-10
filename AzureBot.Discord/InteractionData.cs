using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureBot.Discord;

public record InteractionData
{
    public InteractionData(string id, string name, IReadOnlyCollection<ApplicationCommandOption> options)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("options")]
    public IReadOnlyCollection<ApplicationCommandOption> Options { get; init; }
}
