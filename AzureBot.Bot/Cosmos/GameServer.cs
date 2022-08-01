using Newtonsoft.Json;

namespace AzureBot.Bot.Cosmos;

public record GameServer
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; init; } = default!;
    [JsonProperty(Required = Required.Always)]
    public string ResourceId { get; init; } = default!;
    [JsonProperty(Required = Required.Always)]
    public string Game { get; init; } = default!;
    [JsonProperty(Required = Required.Always)]
    public string Name { get; init; } = default!;
    [JsonProperty(Required = Required.Always)]
    public string CurrentSku { get; init; } = default!;
}
