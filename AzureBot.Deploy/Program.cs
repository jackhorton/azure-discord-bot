using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzureBot.Deploy
{
    class Program
    {
        const string _deploymentName = "azurebot-deploy";
        static readonly Dictionary<string, JsonElement> _deserializedOutputCache = new();
        static readonly TokenCredential _credentials = new VisualStudioCredential(new VisualStudioCredentialOptions { TenantId = "36f71bd6-cb64-4543-920f-d5ddad13fc2b" });

        static Task Main(string[] args)
        {
            var root = new RootCommand("Deployment utilities for AzureBot");

            var infra = new Command("infra", "Deploys the infra for AzureBot")
            {
                new Option<string>(new[] { "--subscription-id", "-s" }, "The subscription ID for your azure subscription"),
                new Option<string>(new[] { "--resource-group-prefix", "-r" }, "The prefix for both resource groups that get created"),
            };
            infra.Handler = CommandHandler.Create<string, string, IConsole>(DeployInfra);

            root.Add(infra);

            return root.InvokeAsync(args);
        }

        static async Task DeployInfra(string subscriptionId, string resourceGroupPrefix, IConsole console)
        {
            var client = new ResourcesManagementClient(subscriptionId, _credentials);

            var infraGroup = $"{resourceGroupPrefix}-infra";
            var vmGroup = $"{resourceGroupPrefix}-vms";
            console.Out.Write($"Ensuring resource groups {infraGroup} and {vmGroup} exist\n");
            await client.ResourceGroups.CreateOrUpdateAsync(infraGroup, new ResourceGroup("westus2"));
            await client.ResourceGroups.CreateOrUpdateAsync(vmGroup, new ResourceGroup("westus2"));

            console.Out.Write($"Deploying resources to {infraGroup}\n");
            var template = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "azuredeploy.json"));
            var parameters = await GetParametersContent(client, infraGroup);
            var properties = new DeploymentProperties(DeploymentMode.Incremental)
            {
                Template = template,
                Parameters = parameters,
            };
            var deploymentOperation = await client.Deployments.StartCreateOrUpdateAsync(infraGroup, _deploymentName, new Deployment(properties));
            var deployment = await deploymentOperation.WaitForCompletionAsync();

            console.Out.Write($"Deployed resources successfully to {infraGroup}, updating application code in the storage account");
            var deploymentOutput = DeserializeOutputs(deployment);
        }

        /// <summary>
        /// Generates the parameters content for azuredeploy.json. Specifies the SQL server password using
        /// a key vault reference if possible, otherwise generates a new password.
        /// </summary>
        /// <returns>The stringified parameters content</returns>
        static async Task<string> GetParametersContent(ResourcesManagementClient client, string resourceGroupName)
        {
            return JsonSerializer.Serialize(new
            {
                sqlServerPassword = await GeneratePasswordParameter(client, resourceGroupName, "sqlServerPasswordName"),
                vmPassword = await GeneratePasswordParameter(client, resourceGroupName, "vmPasswordName"),
            });
        }

        /// <summary>
        /// Generates either a <c>"value": "plaintextpassword"</c> or key vault reference for a securestring parameter.
        /// </summary>
        /// <remarks>See https://docs.microsoft.com/en-us/azure/azure-resource-manager/templates/key-vault-parameter for more information</remarks>
        /// <returns>An opaque object that can be serialized by <see cref="JsonSerializer"/></returns>
        static async Task<object> GeneratePasswordParameter(ResourcesManagementClient client, string resourceGroupName, string secretKeyOutputName)
        {
            try
            {
                var deployment = await client.Deployments.GetAsync(resourceGroupName, _deploymentName);
                if (deployment.Value.Properties.ProvisioningState == "Succeeded")
                {
                    if (TryGetStringOutput(deployment, "keyVaultId", out var kvId) && TryGetStringOutput(deployment, secretKeyOutputName, out var secretName))
                    {
                        return new
                        {
                            reference = new
                            {
                                keyVault = new
                                {
                                    id = kvId
                                },
                                secretName = secretName,
                            }
                        };
                    }
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // this is a new resource group -- thats ok
            }

            return new
            {
                value = GenerateNewPassword(),
            };
        }

        /// <summary>
        /// Generates a secure password for first-time deployments. Subsequent deployments should reuse a previously generated password.
        /// </summary>
        static string GenerateNewPassword()
        {
            using var rng = RandomNumberGenerator.Create();
            var pool = ArrayPool<byte>.Shared;
            var data = pool.Rent(25);
            rng.GetBytes(data);
            var password = Convert.ToBase64String(data);
            pool.Return(data);
            return password;
        }

        /// <summary>
        /// Helper function to get a string output from a deployment.
        /// </summary>
        /// <returns><see langword="true"/> if the output is found, <see langword="false"/> if not. If the output is not found, <paramref name="value"/> will be set to the empty string.</returns>
        static bool TryGetStringOutput(DeploymentExtended deployment, string outputName, out string value)
        {
            var outputs = DeserializeOutputs(deployment);
            if (outputs.TryGetProperty(outputName, out var outputValue))
            {
                value = outputValue.GetProperty("value").GetString() ?? throw new NullReferenceException();
                return true;
            }

            value = "";
            return false;
        }

        static JsonElement DeserializeOutputs(DeploymentExtended deployment)
        {
            var correlationId = deployment.Properties.CorrelationId;
            if (_deserializedOutputCache.TryGetValue(correlationId, out var deserialized))
            {
                return deserialized;
            }

            var outputs = JsonDocument.Parse(JsonSerializer.Serialize(deployment.Properties.Outputs)).RootElement;
            _deserializedOutputCache.Add(correlationId, outputs);
            return outputs;
        }
    }
}
