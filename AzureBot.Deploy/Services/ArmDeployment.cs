using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Services;

internal class ArmDeployment
{
    private readonly TokenCredential _credential;
    private readonly ILogger<ArmDeployment> _logger;

    public ArmDeployment(TokenCredential credential, ILogger<ArmDeployment> logger)
    {
        _credential = credential;
        _logger = logger;
    }

    public async virtual Task<DeploymentPropertiesExtended> DeployLocalTemplateAsync<TParameters>(
        string templateName,
        TParameters parameters,
        ResourceIdentifier scope,
        CancellationToken cancellationToken)
    {
        var armClient = new ArmClient(_credential);
        var rgClient = armClient.GetResourceGroup(scope);

        var template = File.ReadAllText(Path.Combine(Assembly.GetExecutingAssembly().Location, "..", templateName + ".json"));
        var props = new DeploymentProperties(DeploymentMode.Incremental)
        {
            Template = JsonDocument.Parse(template).RootElement,
            Parameters = JsonSerializer.SerializeToElement(parameters),
        };

        _logger.LogInformation("Beginning deployment of {}.json", templateName);
        var deploymentOperation = await rgClient.GetDeployments().CreateOrUpdateAsync(templateName, new DeploymentInput(props), cancellationToken: cancellationToken);
        var deployment = await deploymentOperation.WaitForCompletionAsync(cancellationToken);

        var result = deployment.Value.Data.Properties;
        if (result.ProvisioningState != ProvisioningState.Succeeded)
        {
            throw new Exception();
        }
        return result;
    }
}
