using Azure;
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

    public async virtual Task<ArmDeploymentPropertiesExtended> DeployLocalTemplateAsync<TParameters>(
        string templateName,
        TParameters parameters,
        ResourceIdentifier scope,
        CancellationToken cancellationToken)
    {
        var armClient = new ArmClient(_credential);
        var rgClient = armClient.GetResourceGroupResource(scope);

        var template = File.ReadAllText(Path.Combine(Assembly.GetExecutingAssembly().Location, "..", templateName + ".json"));
        var props = new ArmDeploymentProperties(ArmDeploymentMode.Incremental)
        {
            Template = BinaryData.FromString(template),
            Parameters = BinaryData.FromObjectAsJson(parameters),
        };

        _logger.LogInformation("Beginning deployment of {}.json", templateName);
        var deploymentOperation = await rgClient.GetArmDeployments().CreateOrUpdateAsync(WaitUntil.Completed, templateName, new ArmDeploymentInput(props), cancellationToken: cancellationToken);
        var deployment = await deploymentOperation.WaitForCompletionAsync(cancellationToken);

        var result = deployment.Value.Data.Properties;
        if (result.ProvisioningState != ResourcesProvisioningState.Succeeded)
        {
            throw new Exception();
        }
        return result;
    }
}
