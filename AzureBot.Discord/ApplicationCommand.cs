using System.Text.Json.Serialization;

namespace AzureBot.Discord;
public record ApplicationCommand
{
    public ApplicationCommand(string name, string description)
    {
        Name = name;
        Description = description;
    }

    [JsonPropertyName("id")]
    public string? Id { get; init; }
    [JsonPropertyName("type")]
    public ApplicationCommandType Type => ApplicationCommandType.ChatInput;
    [JsonPropertyName("application_id")]
    public string? ApplicationId { get; init; }
    [JsonPropertyName("guild_id")]
    public string? GuildId { get; init; }
    [JsonPropertyName("name")]
    public string Name { get; init; }
    [JsonPropertyName("description")]
    public string Description { get; init; }
    [JsonPropertyName("options")]
    public IReadOnlyCollection<ApplicationCommandOption>? Options { get; init; }
}
