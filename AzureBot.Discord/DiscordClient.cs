using Azure.Core;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AzureBot.Discord;

public class DiscordClient
{
    private readonly ILogger<DiscordClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly IDiscordAuthentication _authentication;

    public DiscordClient(ILogger<DiscordClient> logger, HttpClient httpClient, IDiscordAuthentication authentication)
    {
        _logger = logger;
        _httpClient = httpClient;
        _authentication = authentication;
    }

    public Task NewGuildCommandAsync(string applicationId, string guildId, ApplicationCommand command, CancellationToken cancellationToken)
    {
        var url = new Uri($"https://discord.com/api/v8/applications/{applicationId}/guilds/{guildId}/commands");
        return NewCommandInternalAsync(url, command, cancellationToken);
    }

    public Task NewCommandAsync(string applicationId, ApplicationCommand command, CancellationToken cancellationToken)
    {
        var url = new Uri($"https://discord.com/api/v8/applications/{applicationId}/commands");
        return NewCommandInternalAsync(url, command, cancellationToken);
    }

    private async Task NewCommandInternalAsync(Uri url, ApplicationCommand command, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, command, cancellationToken: cancellationToken);
        stream.Seek(0, SeekOrigin.Begin);

        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content,
        };

        await SendAsync(req, cancellationToken);

        _logger.LogInformation("Command updated successfully");
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var botToken = await _authentication.GetBotTokenAsync(cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
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
