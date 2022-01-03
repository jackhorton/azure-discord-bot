using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using AzureBot.Deploy.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands;

internal class UpdateCommandCommand : ICommandHandler
{
    private static readonly Option<InstanceParameter> _instanceOption = new(new[] { "--instance", "-i" }, "The configuration file for the instance you are deploying");
    private static readonly Option<string> _commandNameOption = new(new[] { "--name", "-n" }, "The command to update");

    public static Command GetCommand(IServiceProvider serviceProvider)
    {
        var command = new Command("update-command", "Creates or updates a discord bot command")
        {
            _instanceOption,
            _commandNameOption,
        };
        command.Handler = ActivatorUtilities.CreateInstance<UpdateCommandCommand>(serviceProvider);
        return command;
    }

    private readonly JsonObject _helloWorldCommand = new()
    {
        ["name"] = "hello-world",
        ["description"] = "A basic command",
    };
    private readonly ILogger<UpdateCommandCommand> _logger;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;

    public UpdateCommandCommand(ILogger<UpdateCommandCommand> logger, IHttpClientFactory httpClientFactory, TokenCredential credential)
    {
        _logger = logger;
        _credential = credential;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        var cancellationToken = context.GetCancellationToken();
        var instance = context.ParseResult.GetValueForOption(_instanceOption)?.Instance ?? throw new Exception();

        var secretClient = new SecretClient(instance.Discord.BotTokenVault, _credential);
        var botToken = await secretClient.GetSecretAsync(instance.Discord.BotTokenSecretName, cancellationToken: cancellationToken);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            _helloWorldCommand.WriteTo(writer);
        }
        stream.Seek(0, SeekOrigin.Begin);

        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://discord.com/api/v8/applications/{instance.Discord.ApplicationId}/commands")
        {
            Content = content,
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken.Value.Value);
        var response = await _httpClient.SendAsync(req);
        try
        {
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Command updated successfully");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Command update failed with response code {} {}: \n{}",
                response.StatusCode,
                response.ReasonPhrase,
                await response.Content.ReadAsStringAsync(cancellationToken));
            return 1;
        }
    }
}
