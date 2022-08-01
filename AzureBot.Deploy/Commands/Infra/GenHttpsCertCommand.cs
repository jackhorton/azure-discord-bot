using Azure.Core;
using AzureBot.CommandLine;
using AzureBot.Deploy.Acme;
using AzureBot.Deploy.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands.Infra;

[GeneratedCommand("gen-cert", "Creates or updates the ACME HTTPS certificate")]
public partial class GenHttpsCertCommand : ICommandHandler
{
    private static readonly Option<InstanceConfig> _instanceOption = new(
        new[] { "--instance", "-i" },
        InstanceConfig.FromArgument,
        false,
        "The configuration file for the instance containing the certificate. Must not use with other options.");
    private static readonly Option<string> _domainOption = new(new[] { "--domain", "-d" }, "The domain to generate the certificate for. Must not use with --instance.");
    private static readonly Option<string> _subdomainOption = new(new[] { "--subdomain", "-s" }, "The subdomain to generate the certificate for. Must not use with --instance.");
    private static readonly Option<string> _emailOption = new(new[] { "--email", "-e" }, "The email to associate with the ACME account. Must not use with --instance.");
    private static readonly Option<Uri> _directoryOption = new(new[] { "--directory", "-D" }, "The issuing ACME directory. Must not use with --instance.");
    private static readonly Option<Uri> _keyVaultUrlOption = new(new[] { "--key-vault-url", "-k" }, "The key vault URL for storing the certificate and account key. Must not use with --instance.");
    private static readonly Option<string> _resourceGroupIdOption = new(new[] { "--resource-group", "-g" }, "The resource group containing the Azure DNS zone. Must not use with --instance.");
    private static readonly Option<AcmeCertificateFormat?> _formatOption = new(new[] { "--format", "-f" }, "The format to store the certificate in. One of 'pkcs12' or 'pem'");
    private static readonly Option<string[]> _sanOption = new(new[] { "--alternate-name", "--san", "-S" }, "Alternate names to register the certificate for") { Arity = ArgumentArity.ZeroOrMore };

    private readonly ILogger<GenHttpsCertCommand> _logger;
    private readonly AcmeCertificateGenerator _acmeCertificateGenerator;

    public GenHttpsCertCommand(ILogger<GenHttpsCertCommand> logger, AcmeCertificateGenerator acmeCertificateGenerator)
    {
        _logger = logger;
        _acmeCertificateGenerator = acmeCertificateGenerator;
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        var cancellationToken = context.GetCancellationToken();
        AcmeOptions options;
        try
        {
            var instance = context.ParseResult.GetValueForOption(_instanceOption) ?? throw new Exception();

            var keyVaultUrl = new Uri("https://azurebot-kvhhirfsxqfdckg.vault.azure.net");
            var resourceGroupId = new ResourceIdentifier($"/subscriptions/{instance.SubscriptionId}/resourceGroups/{instance.ResourceGroupName}");

            options = new AcmeOptions(
                instance.Domain,
                instance.ControllerName,
                instance.Https.Email,
                instance.Https.Directory,
                keyVaultUrl,
                resourceGroupId,
                AcmeCertificateFormat.Pem,
                Array.Empty<string>());
        }
        catch
        {
            options = new AcmeOptions(
                context.ParseResult.GetValueForOption(_domainOption) ?? throw new Exception(),
                context.ParseResult.GetValueForOption(_subdomainOption),
                context.ParseResult.GetValueForOption(_emailOption) ?? throw new Exception(),
                context.ParseResult.GetValueForOption(_directoryOption) ?? new Uri("https://acme-v02.api.letsencrypt.org/directory"),
                context.ParseResult.GetValueForOption(_keyVaultUrlOption) ?? throw new Exception(),
                new ResourceIdentifier(context.ParseResult.GetValueForOption(_resourceGroupIdOption) ?? throw new Exception()),
                context.ParseResult.GetValueForOption(_formatOption) ?? throw new Exception(),
                context.ParseResult.GetValueForOption(_sanOption) ?? Array.Empty<string>());
        }

        var certUrl = await _acmeCertificateGenerator.GenerateHttpsCertificateAsync(options, cancellationToken);
        _logger.LogInformation("Final certificate name is {name}", certUrl);

        return 0;
    }


}
