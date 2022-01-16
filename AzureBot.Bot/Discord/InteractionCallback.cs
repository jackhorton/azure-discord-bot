using System.Text.Json.Serialization;

namespace AzureBot.Bot.Discord;

public record InteractionCallback
{
    [JsonPropertyName("type")]
    public InteractionCallbackType Type { get; init; }

    [JsonPropertyName("data")]
    public InteractionCallbackData Data { get; init; }

    public static InteractionCallback Pong()
    {
        return new InteractionCallback
        {
            Type = InteractionCallbackType.Pong,
        };
    }

    public static InteractionCallback Message(string content)
    {
        return new InteractionCallback
        {
            Type = InteractionCallbackType.ChannelMessageWithSource,
            Data = new InteractionCallbackData { Content = content },
        };
    }
}
