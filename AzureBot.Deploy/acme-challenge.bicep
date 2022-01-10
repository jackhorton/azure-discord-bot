param keyVaultName string

@secure()
param accountKey string

param challengeRecordContent string

@description('The domain name to link this deployment to')
param dnsZoneName string

param recordName string

resource dnsZone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: dnsZoneName
}
resource dnsRecord 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  name: recordName
  parent: dnsZone
  properties: {
    TTL: 300
    TXTRecords: [
      {
        value: [
          challengeRecordContent
        ]
      }
    ]
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2021-06-01-preview' existing = {
  name: keyVaultName
}
resource accountKeySecret 'Microsoft.KeyVault/vaults/secrets@2021-06-01-preview' = {
  name: 'acme-accountkey-pem'
  parent: keyVault
  properties: {
    value: accountKey
  }
}
