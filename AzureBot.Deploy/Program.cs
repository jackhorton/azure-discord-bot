using Azure.Core;
using Azure.Identity;
using AzureBot.Deploy.Commands;
using AzureBot.Deploy.Services;
using DnsClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Threading.Tasks;

namespace AzureBot.Deploy;

internal class Program
{
    private static readonly TokenCredential _credentials = new VisualStudioCredential(new VisualStudioCredentialOptions { TenantId = "36f71bd6-cb64-4543-920f-d5ddad13fc2b" });

    public static Task Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging((builder) =>
        {
            builder.AddSimpleConsole(console =>
            {
                console.SingleLine = true;
            });
        });
        services.AddSingleton(_credentials);
        services.AddSingleton<ArmDeployment>();
        services.AddSingleton<ILookupClient, LookupClient>();
        services.AddSingleton<AcmeCertificateGenerator>();
        services.AddHttpClient();
        var serviceProvider = services.BuildServiceProvider();

        var root = new RootCommand("Deployment utilities for AzureBot")
        {
            DeployInfraCommand.GetCommand(serviceProvider),
            UpdateCommandCommand.GetCommand(serviceProvider),
            GenHttpsCertCommand.GetCommand(serviceProvider),
        };

        return root.InvokeAsync(args);
    }
}
