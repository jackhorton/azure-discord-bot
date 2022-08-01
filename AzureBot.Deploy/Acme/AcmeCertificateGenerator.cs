using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using AzureBot.Deploy.Services;
using Certes;
using Certes.Acme;
using DnsClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Acme;

public class AcmeCertificateGenerator
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

    private record DnsChallenge(IChallengeContext Challenge, string FullDnsName, string RecordName);

    public async virtual Task<string> GenerateHttpsCertificateAsync(
        AcmeOptions options,
        CancellationToken cancellationToken)
    {
        const string certName = "acme-https-cert";
        var certs = new CertificateClient(options.KeyVaultUrl, _credential);
        try
        {
            var cert = await certs.GetCertificateAsync(certName, cancellationToken);
            var thumbprint = Convert.ToHexString(cert.Value.Properties.X509Thumbprint);
            if (cert.Value.Properties.ExpiresOn > DateTimeOffset.UtcNow.AddDays(30) && (cert.Value.Properties.Enabled ?? false))
            {
                _logger.LogInformation("Existing valid HTTPS certificate has been found with thumbprint {}", thumbprint);
                return cert.Value.Name;
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
            var secrets = new SecretClient(options.KeyVaultUrl, _credential);
            var accountKey = await secrets.GetSecretAsync(accountKeySecretName, cancellationToken: cancellationToken);
            acme = new AcmeContext(options.DirectoryUrl, KeyFactory.FromPem(accountKey.Value.Value));
            _logger.LogDebug("Found existing ACME account");
        }
        catch
        {
            _logger.LogInformation("Creating new ACME account at {} with email {}", options.DirectoryUrl, options.AccountEmail);
            acme = new AcmeContext(options.DirectoryUrl);
            await acme.NewAccount(options.AccountEmail, termsOfServiceAgreed: true);
        }

        string orderIdentifier;
        string acmeChallengeRecord;
        if (options.Subdomain is { Length: > 0 })
        {
            orderIdentifier = $"{options.Subdomain}.{options.ZoneName}";
            acmeChallengeRecord = $"_acme-challenge.{options.Subdomain}";
        }
        else
        {
            orderIdentifier = options.ZoneName;
            acmeChallengeRecord = "_acme-challenge";
        }
        var order = await acme.NewOrder((new[] { orderIdentifier }).Concat(options.AlternateNames).ToArray());

        var authorizations = await order.Authorizations();
        var dnsChallenges = new List<DnsChallenge>();
        foreach (var auth in authorizations)
        {
            var challenge = await auth.Dns();
            var resource = await auth.Resource();
            if (!resource.Identifier.Value.EndsWith(options.ZoneName))
            {
                throw new Exception($"Challenge presented for {resource.Identifier.Value}, which is outside of root domain {options.ZoneName}");
            }

            var fullAcmeChallengeUrl = $"_acme-challenge.{resource.Identifier.Value}";
            var recordName = fullAcmeChallengeUrl[..^(options.ZoneName.Length + 1)];
            dnsChallenges.Add(new DnsChallenge(challenge, fullAcmeChallengeUrl, recordName));
        }

        await _armDeployment.DeployLocalTemplateAsync(
            "acme-challenge",
            new Dictionary<string, object?>
            {
                ["keyVaultName"] = options.KeyVaultUrl.Host.Split('.').First(),
                ["accountKey"] = acme.AccountKey.ToPem(),
                ["dnsZoneName"] = options.ZoneName,
                ["challenges"] = dnsChallenges.Select((challenge) => new Dictionary<string, string>
                {
                    ["name"] = challenge.RecordName,
                    ["text"] = acme.AccountKey.DnsTxt(challenge.Challenge.Token),
                })
            },
            options.ResourceGroupId,
            cancellationToken);

        _logger.LogInformation("Waiting up to 5 minutes for DNS challenge to propagate");
        var propagationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        propagationTimeout.CancelAfter(TimeSpan.FromMinutes(5));
        while (!propagationTimeout.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), propagationTimeout.Token);
            var result = await _lookupClient.QueryAsync(dnsChallenges.First().FullDnsName, QueryType.TXT, cancellationToken: propagationTimeout.Token);
            if (result.Answers.TxtRecords().Any((txt) => txt.Text.FirstOrDefault() == acme.AccountKey.DnsTxt(dnsChallenges.First().Challenge.Token)))
            {
                break;
            }
        }
        propagationTimeout.Token.ThrowIfCancellationRequested();

        _logger.LogInformation("The DNS challenge record has been found locally. Waiting an additional 5 minutes so that the ACME directory is more likely to also observe the DNS challenge");
        await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);

        _logger.LogInformation("Validating DNS challenge(s)");
        foreach (var challenge in dnsChallenges)
        {
            await challenge.Challenge.Validate();
        }

        _logger.LogInformation("Generating a CSR from key vault");
        var sans = new SubjectAlternativeNames();
        sans.DnsNames.Add(orderIdentifier);
        foreach (var san in options.AlternateNames)
        {
            sans.DnsNames.Add(san);
        }
        var keyVaultCertOperation = await certs.StartCreateCertificateAsync(
            certName,
            new CertificatePolicy(WellKnownIssuerNames.Unknown, $"CN={orderIdentifier}", sans)
            {
                ContentType = options.Format == AcmeCertificateFormat.Pkcs12 ? CertificateContentType.Pkcs12 : CertificateContentType.Pem,
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
            return merged.Value.Name;
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
