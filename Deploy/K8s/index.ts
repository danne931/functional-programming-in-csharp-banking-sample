import * as pulumi from '@pulumi/pulumi'
import * as k8s from '@pulumi/kubernetes'

import initPostgres from './postgres'
import { initBankEnvConfigMap, isDev } from './environment'
import { initAccountCluster, initAccountService } from './account'
import { initServiceAccount, initAkkaRBAC } from './admin'
import { initMockThirdPartyBankService, initMockThirdPartyBankDeployment } from './mock-third-party-bank'
import { initSchedulerCluster, initSchedulerService } from './scheduler'
import { initWebCluster, initWebService, initLocalWebLoadBalancer, initIngress } from './web'

const configStore = new pulumi.Config()
const currentStack = pulumi.getStack()
const namespace = new k8s.core.v1.Namespace(
  'ns',
  {
    metadata: {
      name: configStore.require('defaultK8Namespace')
    }
  }
)

let k8sProvider = null
if (currentStack === 'k8s-local') {
  k8sProvider = new k8s.Provider(
    'k8s-provider',
    {
      namespace: namespace.metadata.name,
      context: 'minikube'
    }
  )
} else if (currentStack === 'k8s-staging') {
  const azureInfraStack = new pulumi.StackReference(
    `${pulumi.getOrganization()}/${pulumi.getProject()}/azure-staging`
  )
  const kubeconfig = azureInfraStack.getOutput('kubeconfig')

  k8sProvider = new k8s.Provider(
    'k8s-provider',
    {
      kubeconfig,
      namespace: namespace.metadata.name
    }
  )
}

if (k8sProvider == null) {
  throw new Error('Cannot determine provider.  Expected k8s-staging or k8s-local stack.')
}

// Postgres Helm Chart
// TODO:
// -Currently using Postgres via helm chart for local & staging.
//  Research using Azure's Postgres for staging.
initPostgres(k8sProvider, namespace)

const bankEnvConfigMap = initBankEnvConfigMap(k8sProvider)
initServiceAccount(k8sProvider)
initAkkaRBAC(k8sProvider)
initMockThirdPartyBankService(k8sProvider)
initMockThirdPartyBankDeployment(k8sProvider)
initAccountCluster(k8sProvider, bankEnvConfigMap)
initAccountService(k8sProvider)
initSchedulerCluster(k8sProvider, bankEnvConfigMap)
initSchedulerService(k8sProvider)
initWebCluster(k8sProvider, bankEnvConfigMap)
initWebService(k8sProvider)

let url = null
if (isDev) {
  const loadBalancer = initLocalWebLoadBalancer(k8sProvider)
  url = loadBalancer.status.apply(o => o.loadBalancer.ingress[0].ip)
} else {
  // TODO: https; host
  const ingress = initIngress(k8sProvider)
  url = ingress.status.apply(o =>
    o.loadBalancer.ingress[0].hostname ?? o.loadBalancer.ingress[0].ip
  )
}

export const endpoint = url
