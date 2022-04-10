using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using AzureBot.Deploy.Configuration;
using AzureBot.Discord;
using System.Threading;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands.Discord;
internal class DiscordAuthentication : IDiscordAuthentication
{
    private readonly SecretClient _secretClient;
    private readonly string _secretName;

    public DiscordAuthentication(TokenCredential tokenCredential, InstanceConfig config)
    {
        _secretClient = new SecretClient(config.Discord.BotTokenVault, tokenCredential);
        _secretName = config.Discord.BotTokenSecretName;
    }

    public async Task<string> GetBotTokenAsync(CancellationToken cancellationToken)
    {
        var secret = await _secretClient.GetSecretAsync(_secretName, cancellationToken: cancellationToken);
        return secret.Value.Value;
    }
}
