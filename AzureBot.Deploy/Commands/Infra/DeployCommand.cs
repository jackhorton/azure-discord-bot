using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureBot.Deploy.Acme;
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

namespace AzureBot.Deploy.Commands.Infra;

internal class DeployCommand : ICommandHandler
{
    private static readonly Option<InstanceConfig> _instanceOption = new(new[] { "--instance", "-i" }, InstanceConfig.FromArgument, false, "The configuration file for the instance you are deploying") { IsRequired = true };

    public static Command GetCommand(IServiceProvider serviceProvider)
    {
        var command = new Command("deploy", "Creates or updates the bot controller infrastructure")
        {
            _instanceOption,
        };
        command.Handler = ActivatorUtilities.CreateInstance<DeployCommand>(serviceProvider);
        return command;
    }

    private readonly ILogger<DeployCommand> _logger;
    private readonly ArmDeployment _armDeployment;
    private readonly TokenCredential _credential;
    private readonly AcmeCertificateGenerator _acmeCertificateGenerator;

    public DeployCommand(ILogger<DeployCommand> logger, ArmDeployment armDeployment, TokenCredential credential, AcmeCertificateGenerator acmeCertificateGenerator)
    {
        _logger = logger;
        _armDeployment = armDeployment;
        _credential = credential;
        _acmeCertificateGenerator = acmeCertificateGenerator;
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        var cancellationToken = context.GetCancellationToken();
        var instance = context.ParseResult.GetValueForOption(_instanceOption) ?? throw new Exception();

        var publishDirectory = await PublishBotAppAsync(cancellationToken);

        var armClient = new ArmClient(_credential);
        var resourceGroupId = new ResourceIdentifier($"/subscriptions/{instance.SubscriptionId}/resourceGroups/{instance.ResourceGroupName}");

        _logger.LogInformation("Ensuring resource group");
        var rgUpdateOperation = await armClient
            .GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{instance.SubscriptionId}"))
            .GetResourceGroups()
            .CreateOrUpdateAsync(WaitUntil.Completed, instance.ResourceGroupName, new ResourceGroupData(instance.Location), cancellationToken: cancellationToken);
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

        var baseOutputs = JsonDocument.Parse(baseDeployment.Outputs.ToString()).RootElement;

        var fileUrls = await GetExtensionFilesAsync(baseOutputs, publishDirectory, cancellationToken).ToArrayAsync(cancellationToken);

        var kvName = baseOutputs.GetProperty("keyVaultName").GetProperty("value").GetString();
        var kvUrl = new Uri($"https://{kvName}.vault.azure.net");
        var secretClient = new SecretClient(kvUrl, _credential);

        string publicKeyData;
        try
        {
            var secret = await secretClient.GetSecretAsync("bot-ssh-pub");
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
            await secretClient.SetSecretAsync(new KeyVaultSecret("bot-ssh", File.ReadAllText(keyPath)), cancellationToken);
            publicKeyData = File.ReadAllText($"{keyPath}.pub");
            await secretClient.SetSecretAsync(new KeyVaultSecret("bot-ssh-pub", publicKeyData), cancellationToken);
        }

        var certUrl = await _acmeCertificateGenerator.GenerateHttpsCertificateAsync(
            new AcmeOptions(instance.Domain, instance.ControllerName, instance.Https.Email, instance.Https.Directory, kvUrl, resourceGroupId, AcmeCertificateFormat.Pem, Array.Empty<string>()),
            cancellationToken);

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
                httpsCertUrl = new
                {
                    value = certUrl,
                },
                keyVaultName = new
                {
                    value = kvName
                }
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
            .WithArguments($"publish -c Release -r linux-x64 -o {publishDirectory} --nologo --no-self-contained {botProjectDirectory}")
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
