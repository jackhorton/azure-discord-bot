using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureBot.Deploy.Configuration;
using AzureBot.Deploy.Services;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Command = System.CommandLine.Command;

namespace AzureBot.Deploy.Commands;

internal class DeployInfraCommand : ICommandHandler
{
    private static readonly Option<InstanceParameter> _instanceOption = new(new[] { "--instance", "-i" }, "The configuration file for the instance you are deploying");

    public static Command GetCommand(IServiceProvider serviceProvider)
    {
        var command = new Command("update-infra", "Creates or updates the bot controller infrastructure")
        {
            _instanceOption,
        };
        command.Handler = ActivatorUtilities.CreateInstance<DeployInfraCommand>(serviceProvider);
        return command;
    }

    private readonly ILogger<DeployInfraCommand> _logger;
    private readonly ArmDeployment _armDeployment;
    private readonly TokenCredential _credential;

    public DeployInfraCommand(ILogger<DeployInfraCommand> logger, ArmDeployment armDeployment, TokenCredential credential)
    {
        _logger = logger;
        _armDeployment = armDeployment;
        _credential = credential;
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        var cancellationToken = context.GetCancellationToken();
        var instance = context.ParseResult.GetValueForOption(_instanceOption)?.Instance ?? throw new Exception();

        var publishDirectory = await PublishBotAppAsync(cancellationToken);

        var armClient = new ArmClient(_credential);
        var resourceGroupId = new ResourceIdentifier($"/subscriptions/{instance.SubscriptionId}/resourceGroups/{instance.ResourceGroupName}");

        _logger.LogInformation("Ensuring resource group");
        var rgUpdateOperation = await armClient
            .GetSubscription(new ResourceIdentifier($"/subscriptions/{instance.SubscriptionId}"))
            .GetResourceGroups()
            .CreateOrUpdateAsync(instance.ResourceGroupName, new ResourceGroupData(instance.Location), cancellationToken: cancellationToken);
        await rgUpdateOperation.WaitForCompletionAsync();


        var baseDeployment = await _armDeployment.DeployLocalTemplateAsync(
            "infra-base",
            new
            {
                adminObjectId = new
                {
                    value = instance.AdminObjectId,
                },
                dnsZoneName = new
                {
                    value = instance.Domain,
                },
            },
            resourceGroupId,
            cancellationToken);

        var baseOutputs = JsonSerializer.SerializeToElement(baseDeployment.Outputs);

        var fileUrls = await GetExtensionFilesAsync(baseOutputs, publishDirectory, cancellationToken).ToArrayAsync(cancellationToken);

        var kvUrl = baseOutputs.GetProperty("keyVaultUrl").GetProperty("value").GetString();
        var kvClient = new SecretClient(new Uri(kvUrl!), _credential);

        string publicKeyData;
        try
        {
            var secret = await kvClient.GetSecretAsync("bot-ssh-pub");
            publicKeyData = secret.Value.Value;
        }
        catch
        {
            _logger.LogInformation("Generating new SSH key for bot VM");
            var keyPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await Cli
                .Wrap("ssh-keygen")
                .WithArguments((args) => args.Add("-f").Add(keyPath).Add("-b").Add("4096").Add("-t").Add("rsa").Add("-m").Add("PEM").Add("-N").Add(""))
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteBufferedAsync(cancellationToken);
            await kvClient.SetSecretAsync(new KeyVaultSecret("bot-ssh", File.ReadAllText(keyPath)), cancellationToken);
            publicKeyData = File.ReadAllText($"{keyPath}.pub");
            await kvClient.SetSecretAsync(new KeyVaultSecret("bot-ssh-pub", publicKeyData), cancellationToken);
        }

        await _armDeployment.DeployLocalTemplateAsync(
            "infra-controller",
            new
            {
                storageAccountName = new
                {
                    value = baseOutputs.GetProperty("storageAccountName").GetProperty("value").GetString(),
                },
                dnsZoneName = new
                {
                    value = instance.Domain,
                },
                botBackendExtensionFiles = new
                {
                    value = fileUrls,
                },
                sshPublicKeyData = new
                {
                    value = publicKeyData,
                },
                vmName = new
                {
                    value = instance.ControllerName,
                },
            },
            resourceGroupId,
            cancellationToken);

        return 0;
    }

    private async IAsyncEnumerable<Uri> GetExtensionFilesAsync(JsonElement baseOutputs, string publishDirectory, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var containerUrl = baseOutputs.GetProperty("deployContainerUrl").GetProperty("value").GetString();

        var deployContainerClient = new BlobContainerClient(new Uri(containerUrl!), _credential);

        var existingBlobs = await deployContainerClient
            .GetBlobsAsync(cancellationToken: cancellationToken)
            .SelectAwait((blob) => ValueTask.FromResult(deployContainerClient.GetBlobClient(blob.Name).Uri))
            .ToHashSetAsync(cancellationToken);

        _logger.LogInformation("Uploading extension files to {deployContainerUrl}", deployContainerClient.Uri);

        foreach (var file in Directory.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories))
        {
            var url = await UploadExtensionFileAsync(file, publishDirectory, existingBlobs, deployContainerClient, cancellationToken);
            yield return url;
        }
    }

    private async Task<string> PublishBotAppAsync(CancellationToken cancellationToken)
    {
        var botProjectDirectory = Path.GetFullPath(
            Path.Combine(
                Assembly.GetExecutingAssembly().Location,
                "..", "..", "..", "..", "..",
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

        _logger.LogInformation("Publishing {projectDirectory} to {publishDirectory}", botProjectDirectory, publishDirectory);
        await Cli.Wrap("dotnet")
            .WithArguments($"publish -c Release -r linux-x64 -o {publishDirectory} --nologo {botProjectDirectory}")
            .WithValidation(CommandResultValidation.ZeroExitCode)
            .ExecuteAsync(cancellationToken);
        return publishDirectory;
    }

    private async Task<Uri> UploadExtensionFileAsync(string currentFile, string publishDirectory, HashSet<Uri> currentFiles, BlobContainerClient deployContainerClient, CancellationToken cancellationToken)
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

        if (!currentFiles.Contains(blobClient.Uri))
        {
            await blobClient.UploadAsync(
                currentFile,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentHash = hash,
                    },
                },
                cancellationToken);
            _logger.LogInformation("Uploaded {} with MD5 hash {}", relativePath, hexHash);
        }
        else
        {
            _logger.LogDebug("Skipped upload of {} with MD5 hash {} (already exists)", relativePath, hexHash);
        }

        return blobClient.Uri;
    }
}
