using Microsoft.Extensions.DependencyInjection;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands.Infra;

internal class InfraCommand : ICommandHandler
{
    public static Command GetCommand(IServiceProvider serviceProvider)
    {
        var command = new Command("infra", "Infra commands")
        {
            DeployCommand.GetCommand(serviceProvider),
            GenHttpsCertCommand.GetCommand(serviceProvider),
        };
        command.Handler = ActivatorUtilities.CreateInstance<InfraCommand>(serviceProvider);
        return command;
    }

    public Task<int> InvokeAsync(InvocationContext context)
    {
        throw new NotImplementedException();
    }
}
