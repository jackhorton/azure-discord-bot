param keyVaultName string

@secure()
param accountKey string

@description('The domain name to link this deployment to')
param dnsZoneName string

param challenges array

resource dnsZone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: dnsZoneName
}
resource dnsRecord 'Microsoft.Network/dnsZones/TXT@2018-05-01' = [for challenge in challenges: {
  name: challenge.name
  parent: dnsZone
  properties: {
    TTL: 300
    TXTRecords: [
      {
        value: [
          challenge.text
        ]
      }
    ]
  }
}]

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
