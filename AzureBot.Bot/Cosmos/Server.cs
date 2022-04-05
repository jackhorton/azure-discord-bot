using Newtonsoft.Json;

namespace AzureBot.Bot.Cosmos;

public record Server
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; init; }
    public string GuildId { get; init; }
    public string ResourceId { get; init; }
    public string Game { get; init; }
    public string Name { get; init; }
    public string CurrentSku { get; init; }
}
