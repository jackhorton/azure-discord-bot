using AzureBot.CommandLine;
using AzureBot.Deploy.Configuration;
using AzureBot.Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands.Discord;

[GeneratedCommand("update", "Creates or updates a discord bot command")]
public partial class UpdateCommand : ICommandHandler
{
    private static readonly Option<InstanceConfig> _instanceOption =
        new(new[] { "--instance", "-i" }, InstanceConfig.FromArgument, false, "The configuration file for the instance you are deploying") { IsRequired = true };
    private static readonly Option<string> _commandNameOption = new(new[] { "--name", "-n" }, "The command to update") { IsRequired = true };
    private static readonly Option<string> _guildNameOption = new(
        new[] { "--guild-name", "-g" },
        "The name of the guild to register to. If omitted, the command will be registered globally. Must be one of the WellKnownGuilds in the instance configuration.");

    private readonly ILogger<UpdateCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, ApplicationCommand> _appCommands;

    public UpdateCommand(ILogger<UpdateCommand> logger, IServiceProvider serviceProvider, Dictionary<string, ApplicationCommand> appCommands)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _appCommands = appCommands;
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        var cancellationToken = context.GetCancellationToken();
        var instance = context.ParseResult.GetValueForOption(_instanceOption)!;
        var name = context.ParseResult.GetValueForOption(_commandNameOption)!;
        var guildName = context.ParseResult.GetValueForOption(_guildNameOption);

        var auth = ActivatorUtilities.CreateInstance<DiscordAuthentication>(_serviceProvider, instance);
        var discord = ActivatorUtilities.CreateInstance<DiscordClient>(_serviceProvider, auth);

        if (context.ParseResult.Errors.Any())
        {
            foreach (var error in context.ParseResult.Errors)
            {
                _logger.LogError("{}", error.Message);
            }

            return 1;
        }

        if (guildName is null)
        {
            await discord.NewCommandAsync(instance.Discord.ApplicationId, _appCommands[name], cancellationToken);
        }
        else
        {
            var guilds = instance.Discord.WellKnownGuilds;
            if (guilds is null)
            {
                throw new ArgumentException("--guild-name can only be passed if the given instance has WellKnownGuilds set");
            }
            await discord.NewGuildCommandAsync(instance.Discord.ApplicationId, guilds[guildName], _appCommands[name], cancellationToken);
        }

        return 0;
    }
}
