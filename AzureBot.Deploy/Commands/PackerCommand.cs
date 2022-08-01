using AzureBot.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands;

[GeneratedCommand("packer", "Runs the given packer template to build a VM image")]
internal class PackerCommand : ICommandHandler
{
    public Task<int> InvokeAsync(InvocationContext context)
    {
        throw new System.NotImplementedException();
    }
}
