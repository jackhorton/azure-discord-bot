﻿using Azure.Storage.Queues;
using AzureBot.Bot.Telemetry;
using AzureBot.Discord;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AzureBot.Bot.Queues;

public class VmControlHandler : BackgroundService
{
    private const string _queueName = "control-vm";
    private readonly ILogger<VmControlHandler> _logger;
    private readonly ActivityManager _activityManager;
    private readonly Container _serversContainer;
    private readonly QueueClient _queue;

    private record Message(string FollowupToken, string GuildId, string VmName, VmControlAction Action, string TraceParent);

    public VmControlHandler(ILogger<VmControlHandler> logger, QueueServiceClient queueService, ActivityManager activityManager, CosmosClient cosmos)
    {
        _logger = logger;
        _activityManager = activityManager;
        _serversContainer = cosmos.GetContainer("botdb", "servers");
        _queue = queueService.GetQueueClient(_queueName);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Message message;
            try
            {
                var queueMessage = await _queue.ReceiveMessageAsync(cancellationToken: cancellationToken);
                if (queueMessage is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    continue;
                }

                message = JsonSerializer.Deserialize<Message>(queueMessage.Value.Body)!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to receive message");
                continue;
            }

            using var _ = _activityManager.StartQueueHandler(nameof(VmControlHandler), message.TraceParent);
        }

        throw new NotImplementedException();
    }

    public Task SendAsync(Interaction interaction, string vmName, VmControlAction action, CancellationToken cancellationToken)
    {
        return _queue.SendMessageAsync(
            JsonSerializer.Serialize(new Message(interaction.Token, interaction.GuildId, vmName, action, Activity.Current?.Id ?? "")),
            cancellationToken);
    }
}
