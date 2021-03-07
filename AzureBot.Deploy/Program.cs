using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CliWrap;
using Microsoft.Extensions.FileProviders;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Command = System.CommandLine.Command;

namespace AzureBot.Deploy
{
    class Program
    {
        const string _deploymentName = "azurebot-deploy";
        const string _sqlPasswordSecretName = "SqlServerPassword";
        const string _vmPasswordSecretName = "VmPassword";
        const string _deployContainerName = "deploy";
        static readonly Dictionary<string, JsonElement> _deserializedOutputCache = new();
        static readonly TokenCredential _credentials = new VisualStudioCredential(new VisualStudioCredentialOptions { TenantId = "36f71bd6-cb64-4543-920f-d5ddad13fc2b" });
        static readonly EmbeddedFileProvider _fileProvider = new EmbeddedFileProvider(typeof(Program).Assembly);

        static Task Main(string[] args)
        {
            var root = new RootCommand("Deployment utilities for AzureBot");

            var updateInfra = new Command("update-infra", "Deploys the infra for AzureBot")
            {
                new Option<string>(new[] { "--subscription-id", "-s" }, "The subscription ID for your azure subscription"),
                new Option<string>(new[] { "--resource-group-prefix", "-r" }, "The prefix for both resource groups that get created"),
            };
            updateInfra.Handler = CommandHandler.Create<string, string, IConsole>(UpdateInfrastructure);

            var createInfra = new Command("create-infra", "Creates the skeleton infrastructure for AzureBot but does not deploy any code")
            {
                new Option<string>(new[] { "--subscription-id", "-s" }, "The subscription ID for your azure subscription"),
                new Option<string>(new[] { "--resource-group-prefix", "-r" }, "The prefix for both resource groups that get created"),
                new Option<string>(new[] { "--location", "-l" }, "The location to create resources in"),
            };
            createInfra.Handler = CommandHandler.Create<string, string, string, IConsole>(CreateInfrastructure);

            var commands = new Command("commands", "View and edit Discord bot command registrations");
            var commandUpdate = new Command("update", "Push the given command configuration to Discord")
            {
                new Option<string>(new[] { "--bot-token", "-b" }, "The bot token from the Discord application page"),
                new Option<string>(new[] { "--command", "-c" }, "The name of the command to update. This should mat"),
                new Option<string>(new[] { "--application-id", "-a" }, () => "815335811721592872", "The Discord bot application ID"),
                new Option<string>(new[] { "--guild-id", "-g" }, "The guild ID to apply the command to. If omitted, the command will be registered globally") { IsRequired = false },
            };
            commandUpdate.Handler = CommandHandler.Create<string, string, string, string, IConsole>(UpdateCommand);
            commands.Add(commandUpdate);

            root.Add(updateInfra);
            root.Add(createInfra);
            root.Add(commands);

            return root.InvokeAsync(args);
        }

        static async Task UpdateInfrastructure(string subscriptionId, string resourceGroupPrefix, IConsole console)
        {
            var client = new ResourcesManagementClient(subscriptionId, _credentials);

            var infraGroup = $"{resourceGroupPrefix}-infra";
            var infraResources = await client.Resources.ListByResourceGroupAsync(infraGroup).ToArrayAsync();

            var botProjectDirectory = Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                    "..", "..", "..", "..",
                    "AzureBot.Bot"));
            if (!Directory.Exists(botProjectDirectory))
            {
                throw new Exception($"{botProjectDirectory} does not exist");
            }
            var publishDirectory = Path.Combine(Path.GetTempPath(), "azurebot.bot");
            if (Directory.Exists(publishDirectory))
            {
                Directory.Delete(publishDirectory, true);
            }
            Directory.CreateDirectory(publishDirectory);

            console.Out.Write($"Publishing {botProjectDirectory} to {publishDirectory}... ");
            await Cli.Wrap("dotnet")
                .WithArguments($"publish -c Release -r linux-x64 -o {publishDirectory} --self-contained --nologo /p:PublishSingleFile=true {botProjectDirectory}")
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteAsync();
            console.Out.Write("Done\n");

            // Upload all of the published files to the deploy container if they don't already exist.
            // If any blobs already exist with a prefix matching |hash|, then we can assume that the
            // list of files to give to the extension is exactly equal to the list of blobs with that
            // hash (since the hash is built using the content of every file).
            var storageAccount = infraResources.Single((r) => r.Type == "Microsoft.Storage/storageAccounts");
            var blobServiceClient = new BlobServiceClient(
                new Uri($"https://{storageAccount.Name}.blob.core.windows.net"),
                _credentials);
            var deployContainerClient = blobServiceClient.GetBlobContainerClient(_deployContainerName);
            console.Out.Write($"Uploading extension files to {deployContainerClient.Uri}\n");
            var fileUrls = await Task.WhenAll(Directory
                .EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories)
                .Select((f) => UploadExtensionFile(console, f, publishDirectory, deployContainerClient)));

            var sqlPasswordParameter = GenerateKeyVaultReferenceParameter(infraResources, _sqlPasswordSecretName);
            var vmPasswordParameter = GenerateKeyVaultReferenceParameter(infraResources, _vmPasswordSecretName);
            var deployment = await DeployArmTemplate(client, infraGroup, sqlPasswordParameter, vmPasswordParameter, fileUrls.ToArray(), console);

            console.Out.Write($"Deployed resources successfully to {infraGroup}\n");
            var deploymentOutput = DeserializeOutputs(deployment);
        }

        private static async Task<string> UploadExtensionFile(IConsole console, string currentFile, string publishDirectory, BlobContainerClient deployContainerClient)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(File.ReadAllBytes(currentFile));
            var hexHash = Convert.ToHexString(hash);
            var relativePath = Path.GetRelativePath(publishDirectory, currentFile);
            var blobPath = relativePath
                .Split('\\', '/')
                .Where((p) => !string.IsNullOrWhiteSpace(p))
                // prepend the blob path with the hash of the whole published output
                .Aggregate(hexHash, (blobPath, nextPath) => string.Join('/', blobPath, nextPath));
            var blobClient = deployContainerClient.GetBlobClient(blobPath);

            try
            {
                await blobClient.UploadAsync(currentFile, new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions
                    {
                        IfNoneMatch = ETag.All,
                    },
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentHash = hash,
                    },
                });
                console.Out.Write($"Uploaded {relativePath} with MD5 hash {hexHash}\n");
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.PreconditionFailed || ex.ErrorCode == "BlobAlreadyExists")
            {
                console.Out.Write($"Skipped upload of {relativePath} with MD5 hash {hexHash} (already exists)\n");
            }

            return blobClient.Uri.ToString();
        }

        static async Task CreateInfrastructure(string subscriptionId, string resourceGroupPrefix, string location, IConsole console)
        {
            var client = new ResourcesManagementClient(subscriptionId, _credentials);

            var infraGroup = $"{resourceGroupPrefix}-infra";
            var vmGroup = $"{resourceGroupPrefix}-vms";
            console.Out.Write($"Ensuring resource groups {infraGroup} and {vmGroup} exist\n");
            await client.ResourceGroups.CreateOrUpdateAsync(infraGroup, new ResourceGroup(location));
            await client.ResourceGroups.CreateOrUpdateAsync(vmGroup, new ResourceGroup(location));

            await DeployArmTemplate(client, infraGroup, GenerateNewPasswordParameter(), GenerateNewPasswordParameter(), Array.Empty<string>(), console);
            console.Out.Write($"Deployed resources successfully to {infraGroup}\n");
        }

        static async Task UpdateCommand(string botToken, string command, string applicationId, string guildId, IConsole console)
        {
            var client = new HttpClient();
            using var commandFileContent = _fileProvider.GetFileInfo($"DiscordCommands.{command}.json").CreateReadStream();
            var content = new StreamContent(commandFileContent);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var url = string.IsNullOrEmpty(guildId)
                ? $"https://discord.com/api/v8/applications/{applicationId}/commands"
                : $"https://discord.com/api/v8/applications/{applicationId}/guilds/{guildId}/commands";
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            console.Out.Write(responseBody + "\n");
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Deploys azuredeploy.json with common parameters.
        /// </summary>
        /// <returns>The result of the deployment.</returns>
        static async Task<DeploymentExtended> DeployArmTemplate(
            ResourcesManagementClient client,
            string resourceGroup,
            object sqlPasswordParameter,
            object vmPasswordParameter,
            string[] extensionFiles,
            IConsole console)
        {
            console.Out.Write("Selecting the current deployment user to grant elevated permissions\n");
            var opaqueToken = await _credentials.GetTokenAsync(new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }), default);
            var jwtSecurityToken = new JwtSecurityToken(opaqueToken.Token);
            var oidClaim = jwtSecurityToken.Claims.Single((c) => c.Type == "oid");

            console.Out.Write($"Deploying resources to {resourceGroup}\n");

            using var templateReader = new StreamReader(_fileProvider.GetFileInfo("azuredeploy.json").CreateReadStream());
            var template = templateReader.ReadToEnd();
            var properties = new DeploymentProperties(DeploymentMode.Incremental)
            {
                Template = template,
                Parameters = JsonSerializer.Serialize(new
                {
                    sqlPassword = sqlPasswordParameter,
                    sqlPasswordSecretName = new
                    {
                        value = _sqlPasswordSecretName,
                    },
                    vmPassword = vmPasswordParameter,
                    vmPasswordSecretName = new
                    {
                        value = _vmPasswordSecretName,
                    },
                    botBackendExtensionFiles = new
                    {
                        value = extensionFiles,
                    },
                    deployContainerName = new
                    {
                        value = _deployContainerName,
                    },
                    adminObjectId = new
                    {
                        value = oidClaim.Value,
                    },
                    dnsZoneName = new
                    {
                        value = "groupmeme.xyz",
                    }
                }),
            };
            var deploymentOperation = await client.Deployments.StartCreateOrUpdateAsync(resourceGroup, _deploymentName, new Deployment(properties));
            return await deploymentOperation.WaitForCompletionAsync();
        }

        /// <summary>
        /// Generates either a <c>"value": "plaintextpassword"</c> or key vault reference for a securestring parameter.
        /// </summary>
        /// <remarks>See https://docs.microsoft.com/en-us/azure/azure-resource-manager/templates/key-vault-parameter for more information</remarks>
        /// <returns>An opaque object that can be serialized by <see cref="JsonSerializer"/></returns>
        static object GenerateKeyVaultReferenceParameter(GenericResourceExpanded[] resources, string secretName)
        {
            var keyVaultClient = resources.Single((r) => r.Type == "Microsoft.KeyVault/vaults");
            return new
            {
                reference = new
                {
                    keyVault = new
                    {
                        id = keyVaultClient.Id
                    },
                    secretName,
                }
            };
        }

        /// <summary>
        /// Generates a secure password for first-time deployments. Subsequent deployments should reuse a previously generated password.
        /// </summary>
        static object GenerateNewPasswordParameter()
        {
            using var rng = RandomNumberGenerator.Create();
            var pool = ArrayPool<byte>.Shared;
            var data = pool.Rent(25);
            rng.GetBytes(data);
            var password = Convert.ToBase64String(data);
            pool.Return(data);

            return new
            {
                value = password,
            };
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
