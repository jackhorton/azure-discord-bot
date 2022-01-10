using Azure.ResourceManager;
using AzureBot.Deploy.Configuration;
using AzureBot.Deploy.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands;

internal class GenHttpsCertCommand : ICommandHandler
{
    private static readonly Option<InstanceParameter> _instanceOption = new(new[] { "--instance", "-i" }, "The configuration file for the instance you are deploying");
    private readonly ILogger<GenHttpsCertCommand> _logger;
    private readonly AcmeCertificateGenerator _acmeCertificateGenerator;

    public static Command GetCommand(IServiceProvider serviceProvider)
    {
        var command = new Command("gen-cert", "Creates or updates the bot controller infrastructure")
        {
            _instanceOption,
        };
        command.Handler = ActivatorUtilities.CreateInstance<GenHttpsCertCommand>(serviceProvider);
        return command;
    }

    public GenHttpsCertCommand(ILogger<GenHttpsCertCommand> logger, AcmeCertificateGenerator acmeCertificateGenerator)
    {
        _logger = logger;
        _acmeCertificateGenerator = acmeCertificateGenerator;
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        var cancellationToken = context.GetCancellationToken();
        var instance = context.ParseResult.GetValueForOption(_instanceOption)?.Instance ?? throw new Exception();

        var keyVaultUrl = new Uri("https://azurebot-kvhhirfsxqfdckg.vault.azure.net");
        var resourceGroupId = new ResourceIdentifier($"/subscriptions/{instance.SubscriptionId}/resourceGroups/{instance.ResourceGroupName}");

        var certUrl = await _acmeCertificateGenerator.GenerateHttpsCertificateAsync(
            instance.Domain, instance.ControllerName, instance.Https.Email, instance.Https.Directory, keyVaultUrl, resourceGroupId, cancellationToken);
        _logger.LogInformation("Final certificate URL is {}", certUrl);

        return 0;
    }


}
