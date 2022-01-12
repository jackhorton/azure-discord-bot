using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using Certes;
using Certes.Pkcs;
using DnsClient;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Services;

internal class AcmeCertificateGenerator
{
    private readonly ILogger<AcmeCertificateGenerator> _logger;
    private readonly TokenCredential _credential;
    private readonly ILookupClient _lookupClient;
    private readonly ArmDeployment _armDeployment;

    public AcmeCertificateGenerator(ILogger<AcmeCertificateGenerator> logger, TokenCredential credential, ILookupClient lookupClient, ArmDeployment armDeployment)
    {
        _logger = logger;
        _credential = credential;
        _lookupClient = lookupClient;
        _armDeployment = armDeployment;
    }

    public async virtual Task<Uri> GenerateHttpsCertificateAsync(
        string zoneName,
        string subdomain,
        string accountEmail,
        Uri acmeDirectoryUrl,
        Uri keyVaultUrl,
        ResourceIdentifier resourceGroupId,
        CancellationToken cancellationToken)
    {
        const string certName = "acme-https-cert";
        var certs = new CertificateClient(keyVaultUrl, _credential);
        try
        {
            var cert = await certs.GetCertificateAsync(certName, cancellationToken);
            var thumbprint = Convert.ToHexString(cert.Value.Properties.X509Thumbprint);
            if (cert.Value.Properties.ExpiresOn > DateTimeOffset.UtcNow.AddDays(30))
            {
                _logger.LogInformation("Existing valid HTTPS certificate has been found with thumbprint {}", thumbprint);
                return cert.Value.SecretId;
            }

            _logger.LogWarning("HTTPS certificate {} is expiring soon, automatically generating a new one", thumbprint);
        }
        catch (RequestFailedException)
        {
            _logger.LogInformation("No existing HTTPS certificate has been found, generating a new one");
        }

        const string accountKeySecretName = "acme-accountkey-pem";
        AcmeContext acme;
        try
        {
            var secrets = new SecretClient(keyVaultUrl, _credential);
            var accountKey = await secrets.GetSecretAsync(accountKeySecretName, cancellationToken: cancellationToken);
            acme = new AcmeContext(acmeDirectoryUrl, KeyFactory.FromPem(accountKey.Value.Value));
            _logger.LogDebug("Found existing ACME account");
        }
        catch
        {
            _logger.LogInformation("Creating new ACME account at {} with email {}", acmeDirectoryUrl, accountEmail);
            acme = new AcmeContext(acmeDirectoryUrl);
            await acme.NewAccount(accountEmail, termsOfServiceAgreed: true);
        }

        string orderIdentifier;
        if (subdomain is { Length: > 0 })
        {
            orderIdentifier = $"{subdomain}.{zoneName}";
        }
        else
        {
            orderIdentifier = zoneName;
        }
        var order = await acme.NewOrder(new[] { orderIdentifier });
        var authorizations = await order.Authorizations();
        var dnsChallenge = await authorizations.Single().Dns();
        var dnsText = acme.AccountKey.DnsTxt(dnsChallenge.Token);

        await _armDeployment.DeployLocalTemplateAsync(
            "acme-challenge",
            new
            {
                keyVaultName = new
                {
                    value = keyVaultUrl.Host.Split('.').First(),
                },
                accountKey = new
                {
                    value = acme.AccountKey.ToPem(),
                },
                dnsZoneName = new
                {
                    value = zoneName,
                },
                challengeRecordContent = new
                {
                    value = dnsText,
                },
                recordName = new
                {
                    value = $"_acme-challenge.{subdomain}",
                },
            },
            resourceGroupId,
            cancellationToken);

        _logger.LogInformation("Waiting up to 30 minutes for DNS challenge to propagate");
        var propagationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        propagationTimeout.CancelAfter(TimeSpan.FromMinutes(30));
        while (!propagationTimeout.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), propagationTimeout.Token);
            var result = await _lookupClient.QueryAsync($"_acme-challenge.{subdomain}.{zoneName}", QueryType.TXT, cancellationToken: propagationTimeout.Token);
            if (result.Answers.TxtRecords().Any((txt) => txt.Text.FirstOrDefault() == dnsText))
            {
                break;
            }
        }
        propagationTimeout.Token.ThrowIfCancellationRequested();

        _logger.LogInformation("The DNS challenge record has been found locally. Waiting an additional 5 minutes so that the ACME directory is more likely to also observe the DNS challenge");
        await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);

        _logger.LogInformation("Validating DNS challenge");
        await dnsChallenge.Validate();

        _logger.LogInformation("Generating a CSR from key vault");
        var keyVaultCertOperation = await certs.StartCreateCertificateAsync(
            certName,
            new CertificatePolicy(WellKnownIssuerNames.Unknown, $"CN={orderIdentifier}")
            {
                ContentType = CertificateContentType.Pem,
                KeyType = CertificateKeyType.Rsa,
                Exportable = true,
                ValidityInMonths = 3,
            },
            cancellationToken: cancellationToken);

        try
        {
            _logger.LogInformation("Finalizing the ACME order with the key vault CSR");
            await order.Finalize(keyVaultCertOperation.Properties.Csr);
            var chain = await order.Download();

            var key = KeyFactory.NewKey(KeyAlgorithm.RS256);
            var merged = await certs.MergeCertificateAsync(
                new MergeCertificateOptions(certName, new[] { chain.ToPfx(key).Build(orderIdentifier, "") }),
                cancellationToken);
            return merged.Value.SecretId;
        }
        catch
        {
            // Even if this exception was caused by a cancellation, give the delete operation some time
            var deleteCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await keyVaultCertOperation.DeleteAsync(deleteCts.Token);
            throw;
        }
    }
}
