import * as pulumi from '@pulumi/pulumi'
import * as command from '@pulumi/command'
import { ResourceGroup } from '@pulumi/azure-native/resources'
import { UserAssignedIdentity } from '@pulumi/azure-native/managedidentity'
import { RoleAssignment } from '@pulumi/azure-native/authorization'
import {
  ManagedCluster,
  listManagedClusterAdminCredentialsOutput
} from '@pulumi/azure-native/containerservice'
import * as tls from '@pulumi/tls'

const configStore = new pulumi.Config()
const numWorkerNodes = configStore.getNumber('numWorkerNodes') ?? 2
const k8sVersion = configStore.get('kubernetesVersion') ?? '1.28'
const prefixForDns = configStore.get('prefixForDns') ?? 'pulumi'
const nodeVmSize = configStore.get('nodeVmSize') ?? 'Standard_DS2_v2'
const resourceGroupLocation =
  configStore.get('resourceGroupLocation') ?? 'westus3'
const resourceGroupName = configStore.get('resourceGroupName') ?? 'rg'

const resourceGroup = new ResourceGroup('resourceGroup', {
  location: resourceGroupLocation,
  resourceGroupName
})

const identity = new UserAssignedIdentity('identity', {
  resourceGroupName: resourceGroup.name
})

// Grant the 'contributor' role to the identity on the resource group
// eslint-disable-next-line no-new
new RoleAssignment('roleAssignment', {
  principalId: identity.principalId,
  principalType: 'ServicePrincipal',
  // https://www.azadvertizer.net/azrolesadvertizer/b24988ac-6180-42a0-ab88-20f7382dd24c.html
  roleDefinitionId:
    '/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c',
  scope: resourceGroup.id
})

const privateKey = new tls.PrivateKey('privateKey', {
  algorithm: 'RSA',
  rsaBits: 4096
})

const cluster = new ManagedCluster('managedCluster', {
  resourceName: 'akkaBankCluster',
  resourceGroupName: resourceGroup.name,
  agentPoolProfiles: [
    {
      count: numWorkerNodes,
      enableNodePublicIP: false,
      mode: 'System',
      name: 'systempool',
      osType: 'Linux',
      osDiskSizeGB: 30,
      type: 'VirtualMachineScaleSets',
      vmSize: nodeVmSize
    }
  ],
  dnsPrefix: prefixForDns,
  enableRBAC: true,
  identity: {
    type: 'UserAssigned',
    userAssignedIdentities: [identity.id]
  },
  kubernetesVersion: k8sVersion,
  linuxProfile: {
    adminUsername: 'azureuser',
    ssh: {
      publicKeys: [
        {
          keyData: privateKey.publicKeyOpenssh
        }
      ]
    }
  }
})

pulumi
  .all([cluster.name, resourceGroup.name])
  .apply(([clusterName, rgName]) => {
    // NOTE: Not using Helm ingress-nginx chart to create an ingress controller.
    // Instead, decorate the K8s Ingress resource with
    // { ingressClassName: 'webapprouting.kubernetes.azure.com' } & enable
    // the application routing add-on below.
    // eslint-disable-next-line no-new
    new command.local.Command('enable-app-routing', {
      create: `az aks approuting enable -g ${rgName} -n ${clusterName}`
    })

    // Modify the local kubeconfig file to include the details needed to
    // connect to the AKS cluster.
    // eslint-disable-next-line no-new
    new command.local.Command('get-aks-credentials', {
      create: `az aks get-credentials --resource-group ${rgName} --name ${clusterName}`
    })
  })

export const kubeconfig = buildKubeConfig(resourceGroup, cluster)

// Build a Kubeconfig to access the cluster
function buildKubeConfig (
  resourceGroup: ResourceGroup,
  cluster: ManagedCluster
): pulumi.Output<string> {
  const creds = listManagedClusterAdminCredentialsOutput({
    resourceGroupName: resourceGroup.name,
    resourceName: cluster.name
  })

  const encoded = creds.kubeconfigs[0].value
  const decoded = encoded.apply((enc: string) =>
    Buffer.from(enc, 'base64').toString()
  )
  return decoded
}
