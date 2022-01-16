namespace AzureBot.Bot.Queues;

public record VmControlMessage
{
    public string FollowupToken { get; init; }
    public string VmName { get; init; }
    public VmControlAction Action { get; init; }
}
