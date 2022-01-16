@description('The object ID of the user deploying the template to grant elevated privileges')
param adminObjectId string

@description('The domain name to link this deployment to')
param dnsZoneName string

var unique = uniqueString(resourceGroup().id, subscription().id)
var vnetName = 'azurebot-bot-vnet'
var subnetName = 'azurebot-bot-subnet'

resource storage 'Microsoft.Storage/storageAccounts@2019-06-01' = {
  name: 'azurebot${unique}'
  location: resourceGroup().location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2019-06-01' = {
  parent: storage
  name: 'default'
  properties: {}
}

resource deployContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2019-06-01' = {
  parent: blobServices
  name: 'deployfiles'
  properties: {
    publicAccess: 'None'
  }
}

resource queueServices 'Microsoft.Storage/storageAccounts/queueServices@2021-06-01' = {
  name: 'default'
  parent: storage
}

resource startQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2021-06-01' = {
  name: 'start-vm'
  parent: queueServices
}

resource stopQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2021-06-01' = {
  name: 'stop-vm'
  parent: queueServices
}

resource appInsights 'Microsoft.Insights/components@2020-02-02-preview' = {
  name: 'azurebot-insights'
  location: resourceGroup().location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2020-03-01-preview' = {
  name: 'azurebot-workspace'
  location: resourceGroup().location
  properties: {}
}

resource syslogDcr 'Microsoft.Insights/dataCollectionRules@2021-04-01' = {
  name: 'bot-syslog-dcr'
  location: resourceGroup().location
  kind: 'Linux'
  properties: {
    dataSources: {
      syslog: [
        {
          streams: [
            'Microsoft-Syslog'
          ]
          facilityNames: [
            'auth'
            'authpriv'
            'cron'
            'daemon'
            'kern'
            'syslog'
            'user'
          ]
          logLevels: [
            '*'
          ]
          name: 'syslog-source'
        }
      ]
    }
    destinations: {
      logAnalytics: [
        {
          workspaceResourceId: workspace.id
          name: workspace.name
        }
      ]
    }
    dataFlows: [
      {
        streams: [
          'Microsoft-Syslog'
        ]
        destinations: [
          workspace.name
        ]
      }
    ]
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2019-09-01' = {
  name: 'azurebot-kv${unique}'
  location: resourceGroup().location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enabledForTemplateDeployment: true
    enableSoftDelete: true
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2021-03-15' = {
  name: 'azurebot-cosmos${unique}'
  location: 'southcentralus'
  kind: 'GlobalDocumentDB'
  properties: {
    consistencyPolicy: {
      defaultConsistencyLevel: 'Eventual'
      maxStalenessPrefix: 1
      maxIntervalInSeconds: 5
    }
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    locations: [
      {
        locationName: 'southcentralus'
        failoverPriority: 0
      }
    ]
    databaseAccountOfferType: 'Standard'
    enableAutomaticFailover: false
  }
}

resource cosmosSqlDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2021-06-15' = {
  name: 'botdb'
  parent: cosmosAccount
  properties: {
    resource: {
      id: 'botdb'
    }
  }
}

resource sqlContainerName 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2021-06-15' = {
  parent: cosmosSqlDb
  name: 'servers'
  properties: {
    resource: {
      id: 'servers'
      partitionKey: {
        paths: [
          '/ResourceId'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
      }
    }
    options: {}
  }
}


resource nsg 'Microsoft.Network/networkSecurityGroups@2020-06-01' = {
  name: 'azurebot-bot-nsg'
  location: resourceGroup().location
  properties: {
    securityRules: [
      {
        name: 'SSH'
        properties: {
          priority: 1000
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '22'
        }
      }
      {
        name: 'HTTP'
        properties: {
          priority: 1001
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '80'
        }
      }
      {
        name: 'HTTPS'
        properties: {
          priority: 1002
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '443'
        }
      }
    ]
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2020-06-01' = {
  name: vnetName
  location: resourceGroup().location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.1.0.0/16'
      ]
    }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: '10.1.0.0/24'
          privateEndpointNetworkPolicies: 'Enabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
    ]
  }
}

resource vmManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2018-11-30' = {
  name: 'azurebot-bot-identity'
  location: resourceGroup().location
}

resource storageBlobDataContributor 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  name: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  scope: subscription()
}
resource adminStorageAccountAccessName 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
  scope: storage
  name: guid(adminObjectId, storageBlobDataContributor.id, storage.id)
  properties: {
    roleDefinitionId: storageBlobDataContributor.id
    principalId: adminObjectId
  }
}

resource storageQueueDataContributor 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  name: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  scope: subscription()
}
resource vmStorageQueueDataContributor 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = {
  name: guid(vmManagedIdentity.id, storageQueueDataContributor.id, storage.id)
  scope: storage
  properties: {
    principalId: vmManagedIdentity.properties.principalId
    roleDefinitionId: storageQueueDataContributor.id
  }
}

resource keyVaultAdministrator 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  name: '00482a5a-887f-4fb3-b363-3b7fe8e74483'
  scope: subscription()
}
resource adminKeyVaultAdministrator 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
  scope: keyVault
  name: guid(adminObjectId, keyVaultAdministrator.id, keyVault.id)
  properties: {
    roleDefinitionId: keyVaultAdministrator.id
    principalId: adminObjectId
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  name: '4633458b-17de-408a-b874-0445c86b69e6'
  scope: subscription()
}
resource vmKeyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = {
  name: guid(vmManagedIdentity.id, keyVaultSecretsUser.id, keyVault.id)
  scope: keyVault
  properties: {
    principalId: vmManagedIdentity.properties.principalId
    roleDefinitionId: keyVaultSecretsUser.id
  }
}

resource dnsZone 'Microsoft.Network/dnsZones@2018-05-01' = {
  name: dnsZoneName
  location: 'global'
}

output keyVaultName string = keyVault.name
output storageAccountName string = storage.name
output deployContainerUrl string = '${storage.properties.primaryEndpoints.blob}${deployContainer.name}'
