namespace AzureBot.Discord;
public interface IDiscordAuthentication
{
    public Task<string> GetBotTokenAsync(CancellationToken cancellationToken);
}
