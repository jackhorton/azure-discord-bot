using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using System;
using System.Buffers;
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

            var outputs = JsonDocument.Parse(JsonSerializer.Serialize(deployment.Value.Properties.Outputs)).RootElement;
            var storageAccountName = outputs.GetProperty("storageAccountName").GetProperty("value").GetString();
            console.Out.Write($"Deployed resources successfully {storageAccountName}, migrating SQL");
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
            string? kvId = null;
            string? secretName = null;
            try
            {
                var deployment = await client.Deployments.GetAsync(resourceGroupName, _deploymentName);
                var outputs = JsonDocument.Parse(JsonSerializer.Serialize(deployment.Value.Properties.Outputs)).RootElement;
                if (deployment.Value.Properties.ProvisioningState == "Succeeded")
                {
                    if (outputs.TryGetProperty("keyVaultId", out var kvOutput))
                    {
                        kvId = kvOutput.GetProperty("value").GetString();
                    }
                    if (outputs.TryGetProperty(secretKeyOutputName, out var secretKeyOutput))
                    {
                        secretName = secretKeyOutput.GetProperty("value").GetString();
                    }
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // this is a new resource group -- thats ok
            }

            return (kvId, secretName) switch
            {
                (string kv, string secret) when !string.IsNullOrEmpty(kv) && !string.IsNullOrEmpty(secret) => new
                {
                    reference = new
                    {
                        keyVault = new
                        {
                            id = kv
                        },
                        secretName = secret,
                    }
                },
                _ => new
                {
                    value = GenerateNewPassword(),
                },
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
    }
}
