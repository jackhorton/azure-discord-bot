using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using AzureBot.Deploy.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Services;

internal class DiscordClient
{
    private readonly ILogger<DiscordClient> _logger;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;

    public DiscordClient(ILogger<DiscordClient> logger, TokenCredential credential, HttpClient httpClient)
    {
        _logger = logger;
        _credential = credential;
        _httpClient = httpClient;
    }

    public async Task NewCommandAsync(DiscordAppConfig config, string? guildName, JsonElement command, CancellationToken cancellationToken)
    {
        var url = guildName switch
        {
            null => $"https://discord.com/api/v8/applications/{config.ApplicationId}/commands",
            _ when config.WellKnownGuilds?.ContainsKey(guildName) ?? false => $"https://discord.com/api/v8/applications/{config.ApplicationId}/guilds/{config.WellKnownGuilds[guildName]}/commands",
            _ => throw new ArgumentException($"Invalid guild name {guildName}", nameof(guildName)),
        };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            command.WriteTo(writer);
        }
        stream.Seek(0, SeekOrigin.Begin);

        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content,
        };

        await SendAsync(config, req, cancellationToken);

        _logger.LogInformation("Command updated successfully");
    }

    public async Task<HttpResponseMessage> SendAsync(DiscordAppConfig config, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var secretClient = new SecretClient(config.BotTokenVault, _credential);
        var botToken = await secretClient.GetSecretAsync(config.BotTokenSecretName, cancellationToken: cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken.Value.Value);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "{} {} failed with {} {}: \n{}",
                request.Method,
                request.RequestUri,
                response.StatusCode,
                response.ReasonPhrase,
                await response.Content.ReadAsStringAsync(cancellationToken));
            throw;
        }
    }
}
