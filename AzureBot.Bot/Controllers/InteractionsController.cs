using Azure.Storage.Queues;
using AzureBot.Bot.Configuration;
using AzureBot.Bot.Cosmos;
using AzureBot.Bot.Queues;
using AzureBot.Discord;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSec.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AzureBot.Bot.Controllers;

[Route("api/[controller]")]
[ApiController]
public class InteractionsController : ControllerBase
{
    private readonly SignatureAlgorithm _verificationAlgorithm = SignatureAlgorithm.Ed25519;
    private readonly ILogger<InteractionsController> _logger;
    private readonly QueueServiceClient _queueService;
    private readonly Container _serversContainer;
    private readonly PublicKey _verificationPublicKey;

    public InteractionsController(ILogger<InteractionsController> logger, QueueServiceClient queueService, CosmosClient cosmosClient, IOptionsSnapshot<AzureBotOptions> options)
    {
        _logger = logger;
        _queueService = queueService;
        _serversContainer = cosmosClient.GetContainer("botdb", "servers");
        _verificationPublicKey = PublicKey.Import(
            _verificationAlgorithm,
            Convert.FromHexString(options.Value.AppPublicKey),
            KeyBlobFormat.RawPublicKey);
    }

    [HttpPost]
    public async Task<IActionResult> PostAsync([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        // Authorizing interactions: https://discord.com/developers/docs/interactions/receiving-and-responding#security-and-authorization
        if (!Request.Headers.TryGetValue("X-Signature-Ed25519", out var sigString))
        {
            return Unauthorized();
        }

        if (!Request.Headers.TryGetValue("X-Signature-Timestamp", out var timestamp))
        {
            return Unauthorized();
        }

        var data = Encoding.UTF8.GetBytes(timestamp + body.RootElement.GetRawText());
        var signature = Convert.FromHexString(sigString);
        if (!_verificationAlgorithm.Verify(_verificationPublicKey, data, signature))
        {
            return Unauthorized();
        }

        var interaction = body.Deserialize<Interaction>() ?? throw new ArgumentException("Interaction body must not be null", nameof(body));
        return Ok(await HandleInteractionAsync(interaction, cancellationToken));
    }

    private async Task<InteractionCallback> HandleInteractionAsync(Interaction interaction, CancellationToken cancellationToken)
    {
        return interaction switch
        {
            { Type: InteractionType.Ping } => InteractionCallback.Pong(),
            { Data.Name: "hello-world" } => InteractionCallback.Message($"Hello, {interaction.Member.User.Username}"),
            { Data.Name: "azurebot" } azurebot => await HandleAzureBotCommandAsync(interaction, azurebot.Data.Options, cancellationToken),
            var unknown => throw new Exception($"Unknown root command {unknown?.Data.Name}"),
        };
    }

    private Task<InteractionCallback> HandleAzureBotCommandAsync(Interaction interaction, IReadOnlyCollection<ApplicationCommandOption> options, CancellationToken cancellationToken)
    {
        return options.SingleOrDefault() switch
        {
            { Name: "server", Type: ApplicationCommandOptionType.SubCommandGroup, Options.Count: 1 } server => HandleServerCommandAsync(interaction, server.Options, cancellationToken),
            var unknown => throw new Exception($"Unknown `/azurebot` subcommand {unknown?.Name}"),
        };
    }

    private Task<InteractionCallback> HandleServerCommandAsync(Interaction interaction, IReadOnlyCollection<ApplicationCommandOption> options, CancellationToken cancellationToken)
    {
        return options.SingleOrDefault() switch
        {
            { Name: "start", Type: ApplicationCommandOptionType.SubCommand, Options.Count: 1 } start => HandleServerControlCommandAsync(interaction, start.Options, VmControlAction.Start, cancellationToken),
            { Name: "stop", Type: ApplicationCommandOptionType.SubCommand, Options.Count: 1 } stop => HandleServerControlCommandAsync(interaction, stop.Options, VmControlAction.Stop, cancellationToken),
            var unknown => throw new Exception($"Unknown `/azurebot server` subcommand {unknown?.Name}"),
        };
    }

    private async Task<InteractionCallback> HandleServerControlCommandAsync(Interaction interaction, IReadOnlyCollection<ApplicationCommandOption> options, VmControlAction action, CancellationToken cancellationToken)
    {
        var name = options.Single((opt) => opt.Name == "name" && opt.Type == ApplicationCommandOptionType.String).Value;
        try
        {
            var server = await _serversContainer.ReadItemAsync<GameServer>(
                string.Join("|", name, interaction.GuildId),
                new PartitionKey(interaction.GuildId),
                cancellationToken: cancellationToken);
            _logger.LogInformation("{action}ing server with resource ID {resourceId}", action, server.Resource.ResourceId);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return InteractionCallback.Message($"Server {name} could not be found");
        }

        return action switch
        {
            VmControlAction.Start => InteractionCallback.Message($"Starting VM {name}..."),
            VmControlAction.Stop => InteractionCallback.Message($"Stopping VM {name}..."),
            _ => throw new Exception($"Unhandled VM control action {action}"),
        };
    }
}
