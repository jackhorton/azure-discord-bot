using Azure.Core;
using System;
using System.Collections.Generic;

namespace AzureBot.Deploy.Acme;

internal record AcmeOptions(
    string ZoneName,
    string? Subdomain,
    string AccountEmail,
    Uri DirectoryUrl,
    Uri KeyVaultUrl,
    ResourceIdentifier ResourceGroupId,
    AcmeCertificateFormat Format,
    IReadOnlyCollection<string> AlternateNames);
