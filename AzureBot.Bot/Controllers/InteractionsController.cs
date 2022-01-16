using Azure.Storage.Queues;
using AzureBot.Bot.Discord;
using AzureBot.Bot.Queues;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly PublicKey _verificationPublicKey;

    public InteractionsController(ILogger<InteractionsController> logger, QueueServiceClient queueService)
    {
        _logger = logger;
        _queueService = queueService;
        _verificationPublicKey = PublicKey.Import(
            _verificationAlgorithm,
            Convert.FromHexString("265c24669b077eb4b2c5778a04f903025626224d50ae7da2d6537d35bd022651"),
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

        var interaction = JsonSerializer.Deserialize<Interaction>(data);
        return Ok(await HandleInteractionAsync(interaction, cancellationToken));
    }

    public async Task<InteractionCallback> HandleInteractionAsync(Interaction interaction, CancellationToken cancellationToken)
    {
        return interaction switch
        {
            { Type: InteractionType.Ping } => InteractionCallback.Pong(),
            { Data: { Name: "hello-world" } } => InteractionCallback.Message($"Hello, {interaction.Member.User.Username}"),
            { Data: { Name: "azurebot" } } azurebot => await HandleAzureBotCommandAsync(interaction, azurebot.Data.Options, cancellationToken),
            var unknown => throw new Exception($"Unknown root command {unknown?.Data.Name}"),
        };
    }

    public Task<InteractionCallback> HandleAzureBotCommandAsync(Interaction interaction, IReadOnlyCollection<ApplicationCommandOption> options, CancellationToken cancellationToken)
    {
        return options.SingleOrDefault() switch
        {
            { Name: "server", Type: ApplicationCommandOptionType.SubCommandGroup} server => HandleServerCommandAsync(interaction, server.Options, cancellationToken),
            var unknown => throw new Exception($"Unknown `/azurebot` subcommand {unknown?.Name}"),
        };
    }

    public Task<InteractionCallback> HandleServerCommandAsync(Interaction interaction, IReadOnlyCollection<ApplicationCommandOption> options, CancellationToken cancellationToken)
    {
        return options.SingleOrDefault() switch
        {
            { Name: "start", Type: ApplicationCommandOptionType.SubCommand } start => HandleServerControlCommandAsync(interaction, start.Options, VmControlAction.Start, cancellationToken),
            { Name: "stop", Type: ApplicationCommandOptionType.SubCommand } stop => HandleServerControlCommandAsync(interaction, stop.Options, VmControlAction.Stop, cancellationToken),
            var unknown => throw new Exception($"Unknown `/azurebot server` subcommand {unknown?.Name}"),
        };
    }

    private async Task<InteractionCallback> HandleServerControlCommandAsync(Interaction interaction,  IReadOnlyCollection<ApplicationCommandOption> options, VmControlAction action, CancellationToken cancellationToken)
    {
        var name = options.Single((opt) => opt.Name == "name" && opt.Type == ApplicationCommandOptionType.String).Value;

        var queue = _queueService.GetQueueClient("vm-control");
        await queue.SendMessageAsync(
            JsonSerializer.Serialize(new VmControlMessage
            {
                FollowupToken = interaction.Token,
                VmName = name,
                Action = action,
            }),
            cancellationToken);

        return action switch
        {
            VmControlAction.Start => InteractionCallback.Message($"Starting VM {name}..."),
            VmControlAction.Stop => InteractionCallback.Message($"Stopping VM {name}..."),
            _ => throw new Exception($"Unhandled VM control action {action}"),
        };
    }
}
