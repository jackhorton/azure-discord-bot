using System;
using System.Diagnostics;

namespace AzureBot.Bot.Telemetry;

public class ActivityManager
{
    private static readonly ActivitySource _source = new("AzureBot.Bot", "1");

    public virtual Activity StartQueueHandler(string name, string parentId)
    {
        return _source.StartActivity(name, ActivityKind.Server, parentId) ?? throw new Exception($"Unable to start a new activity with name {name} and parent ID {parentId}");
    }
}
