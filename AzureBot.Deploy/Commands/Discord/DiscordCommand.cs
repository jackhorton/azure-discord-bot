using Microsoft.Extensions.DependencyInjection;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands.Discord;

internal class DiscordCommand : ICommandHandler
{
    public static Command GetCommand(IServiceProvider serviceProvider)
    {
        var command = new Command("discord", "Discord commands")
        {
            UpdateCommand.GetCommand(serviceProvider),
            CurlCommand.GetCommand(serviceProvider),
        };
        command.Handler = ActivatorUtilities.CreateInstance<DiscordCommand>(serviceProvider);
        return command;
    }

    public Task<int> InvokeAsync(InvocationContext context)
    {
        throw new NotImplementedException();
    }
}
