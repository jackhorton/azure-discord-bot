@description('The content of the public key to log in as admin with')
param sshPublicKeyData string

param botBackendExtensionFiles array = []

param vmName string

@description('The name of the storage account used for deployment files')
param storageAccountName string

@description('The domain name to link this deployment to')
param dnsZoneName string

param httpsCertUrl string

param keyVaultName string

param location string = resourceGroup().location

var unique = uniqueString(vmName, resourceGroup().id, subscription().id)
var vnetName = 'azurebot-bot-vnet'
var subnetName = 'azurebot-bot-subnet'

resource storage 'Microsoft.Storage/storageAccounts@2021-06-01' existing = {
  name: storageAccountName
}

resource deployContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2019-06-01' existing = {
  name: '${storageAccountName}/default/deployfiles'
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2020-03-01-preview' existing = {
  name: 'azurebot-workspace'
}

resource nsg 'Microsoft.Network/networkSecurityGroups@2020-06-01' existing = {
  name: 'azurebot-bot-nsg'
}

resource vnet 'Microsoft.Network/virtualNetworks@2020-06-01' existing = {
  name: 'azurebot-bot-vnet'
}

resource appInsights 'Microsoft.Insights/components@2020-02-02-preview' existing = {
  name: 'azurebot-insights'
}

resource syslogDcr 'Microsoft.Insights/dataCollectionRules@2021-04-01' existing = {
  name: 'bot-syslog-dcr'
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2021-03-15' existing = {
  name: 'azurebot-cosmos${uniqueString(resourceGroup().id, subscription().id)}'
}

resource nic 'Microsoft.Network/networkInterfaces@2020-06-01' = {
  name: '${vmName}-nic'
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'bot-ipconfig'
        properties: {
          subnet: {
            id: resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, subnetName)
          }
          privateIPAllocationMethod: 'Dynamic'
          publicIPAddress: {
            id: ipAddress.id
          }
        }
      }
    ]
    networkSecurityGroup: {
      id: nsg.id
    }
  }
}

resource ipAddress 'Microsoft.Network/publicIPAddresses@2021-05-01' = {
  name: '${vmName}-ip'
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
    dnsSettings: {
      domainNameLabel: 'azurebot${unique}'
    }
    idleTimeoutInMinutes: 4
  }
}

resource vmManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2018-11-30' existing = {
  name: 'azurebot-bot-identity'
}

resource vm 'Microsoft.Compute/virtualMachines@2021-07-01' = {
  name: vmName
  location: location
  properties: {
    hardwareProfile: {
      vmSize: 'Standard_B1s'
    }
    storageProfile: {
      osDisk: {
        createOption: 'FromImage'
        managedDisk: {
          storageAccountType: 'Standard_LRS'
        }
        name: '${vmName}-osdisk'
      }
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-focal'
        sku: '20_04-lts'
        version: 'latest'
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
        }
      ]
    }
    osProfile: {
      computerName: vmName
      #disable-next-line adminusername-should-not-be-literal
      adminUsername: 'azurebot-admin'
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              keyData: sshPublicKeyData
              path: '/home/azurebot-admin/.ssh/authorized_keys'
            }
          ]
        }
      }
    }
  }
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: {
      '${vmManagedIdentity.id}': {}
    }
  }
}

resource storageBlobDataReader 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  name: '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
  scope: subscription()
}
resource vmStorageAccess 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
  scope: deployContainer
  name: guid(vmManagedIdentity.id, storageBlobDataReader.id, deployContainer.id)
  properties: {
    roleDefinitionId: storageBlobDataReader.id
    principalId: vmManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource monitoringContributor 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  name: '749f88d5-cbae-40b8-bcfc-e573ddc772fa'
  scope: subscription()
}
resource workspaceAccess 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = {
  name: guid(vm.id, monitoringContributor.id, workspace.id)
  scope: workspace
  properties: {
    principalId: vm.identity.principalId
    roleDefinitionId: monitoringContributor.id
  }
}

resource installBotExtension 'Microsoft.Compute/virtualMachines/extensions@2019-07-01' = {
  parent: vm
  name: 'DeployAzurebot'
  location: location
  properties: {
    publisher: 'Microsoft.Azure.Extensions'
    type: 'CustomScript'
    typeHandlerVersion: '2.1'
    autoUpgradeMinorVersion: true
    #disable-next-line BCP037
    provisionAfterExtensions: [
      'InstallHttpsCert'
    ]
    protectedSettings: {
      fileUris: botBackendExtensionFiles
      commandToExecute: './install-bot-backend.sh "${keyVaultName}" "${appInsights.properties.ConnectionString}" "${vmManagedIdentity.properties.clientId}" "${subscription().tenantId}" "${storage.properties.primaryEndpoints.queue}" "${cosmosAccount.properties.documentEndpoint}"'
      managedIdentity: {
        clientId: vmManagedIdentity.properties.clientId
      }
    }
  }
  dependsOn: [
    vmStorageAccess
    httpsExtension
  ]
}

resource httpsExtension 'Microsoft.Compute/virtualMachines/extensions@2021-07-01' = {
  name: 'InstallHttpsCert'
  location: location
  parent: vm
  properties: {
    publisher: 'Microsoft.Azure.KeyVault'
    type: 'KeyVaultForLinux'
    typeHandlerVersion: '2.0'
    autoUpgradeMinorVersion: true
    enableAutomaticUpgrade: true
    settings: {
      secretsManagementSettings: {
        observedCertificates: [
          httpsCertUrl
        ]
      }
      authenticationSettings: {
        msiClientId: vmManagedIdentity.properties.clientId
      }
    }
  }
}

resource monitorExtension 'Microsoft.Compute/virtualMachines/extensions@2021-07-01' = {
  name: 'AzureMonitor'
  parent: vm
  location: location
  properties: {
    publisher: 'Microsoft.Azure.Monitor'
    type: 'AzureMonitorLinuxAgent'
    typeHandlerVersion: '1.5'
    autoUpgradeMinorVersion: true
    enableAutomaticUpgrade: true
  }
}

resource dcrAssociation 'Microsoft.Insights/dataCollectionRuleAssociations@2021-04-01' = {
  name: guid(syslogDcr.id, vm.id)
  scope: vm
  properties: {
    dataCollectionRuleId: syslogDcr.id
  }
  dependsOn: [
    monitorExtension
  ]
}

resource dnsZone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: dnsZoneName
}
resource botDnsRecord 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  parent: dnsZone
  name: vmName
  properties: {
    TTL: 3600
    ARecords: [
      {
        ipv4Address: ipAddress.properties.ipAddress
      }
    ]
  }
}
