using Azure.Core;
using Azure.Identity;
using AzureBot.Deploy.Acme;
using AzureBot.Deploy.Commands.Discord;
using AzureBot.Deploy.Commands.Infra;
using AzureBot.Deploy.Services;
using AzureBot.Discord;
using DnsClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        services.AddHttpClient<DiscordClient>();
        services.AddSingleton<ILookupClient, LookupClient>();
        services.AddSingleton<AcmeCertificateGenerator>();
        services.AddSingleton((sp) =>
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());
            return JsonSerializer.Deserialize<Dictionary<string, ApplicationCommand>>(
                File.ReadAllText("commands.json"), options)!;
        });
        var serviceProvider = services.BuildServiceProvider();

        var root = new RootCommand("Deployment utilities for AzureBot")
        {
            InfraCommand.GetCommand(serviceProvider),
            DiscordCommand.GetCommand(serviceProvider),
        };

        return root.InvokeAsync(args);
    }
}
