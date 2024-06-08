import * as pulumi from '@pulumi/pulumi'
import * as k8s from '@pulumi/kubernetes'

import { config as pgConfig } from './postgres'

// Grab some values from the Pulumi stack configuration (or use defaults)
const config = new pulumi.Config()

export const isDev = config.require('environment') === 'Development'
export const isStaging = config.require('environment') === 'Staging'
export const isProduction = config.require('environment') === 'Production'

export const ports = {
  akkaRemoting: config.getNumber('akkaRemotingPort') ?? 8082,
  akkaManagement: config.getNumber('akkaManagementPort') ?? 8558,
  akkaHealthCheckReadiness:
    config.getNumber('akkaHealthCheckReadinessPort') ?? 9998,
  akkaHealthCheckLiveness:
    config.getNumber('akkaHealthCheckLivenessPort') ?? 9999,
  petabridgeCmd: config.getNumber('petabridgeCmdPort') ?? 9110,
  mockDomesticTransferProcessor: config.getNumber('mockDomesticTransferProcessorTcpBindPort') ?? 5007,
  postgres: config.getNumber('postgresPort') ?? 5432,
  webHttp: config.getNumber('webHttp') ?? 80
}

const defaultNamespace = config.require('defaultK8Namespace')

export const initContainers = {
  postgresReady: {
    command: [
      'sh',
      '-c',
      `until nc -z ${pgConfig.k8ResourceName}-postgresql.${defaultNamespace}.svc.cluster.local ${ports.postgres};` +
      'do echo "Waiting for postgres..."; sleep 3; done;\n'
    ],
    image: 'busybox:1.28',
    name: 'init-account-cluster'
  },
  accountServiceReady: {
    command: [
      'sh',
      '-c',
      `until nc -zv account-service.${defaultNamespace}.svc.cluster.local ${ports.akkaHealthCheckReadiness};` +
      'do echo waiting for account cluster formation; sleep 3; done'
    ],
    image: 'busybox:1.28',
    name: 'wait-for-seed-nodes-in-cluster-formation'
  },
  accountReadModelsReady: {
    command: [
      'sh',
      '-c',
      `until psql --host ${pgConfig.k8ResourceName}-postgresql.${defaultNamespace}.svc.cluster.local` +
      ` -d ${pgConfig.database} -U ${pgConfig.user} -p ${ports.postgres} -c "select 1 from accounts";` +
      ' do echo waiting for account records to be seeded; sleep 3; done\n'
    ],
    env: [
      {
        name: 'PGPASSWORD',
        value: pgConfig.password
      }
    ],
    image: 'postgres:16.0',
    name: 'wait-for-accounts-created'
  }

}

// ConfigMap for environment config relevant across all resources
export const initBankEnvConfigMap = (
  provider: k8s.Provider
): k8s.core.v1.ConfigMap =>
  new k8s.core.v1.ConfigMap(
    'bank-env',
    {
      metadata: {
        name: 'bank-env'
      },
      data: {
        ASPNETCORE_ENVIRONMENT: config.require('environment'),
        AkkaRemoting__Port: ports.akkaRemoting.toString(),

        ClusterStartupMethod: 'DiscoveryKubernetes',
        ClusterDiscoveryKubernetesStartup__PodLabelSelector: 'cluster=akkabank',
        ClusterDiscoveryKubernetesStartup__PortName: 'akka-management',
        ClusterDiscoveryKubernetesStartup__RequiredContactPointsNr: (
          config.getNumber('akkaClusterDiscoveryRequiredContactPoints') ?? 2
        ).toString(),

        AkkaHealthCheck__ReadinessPort:
          ports.akkaHealthCheckReadiness.toString(),
        AkkaHealthCheck__LivenessPort: ports.akkaHealthCheckLiveness.toString(),
        PetabridgeCmdRemoting__Port: ports.petabridgeCmd.toString(),

        ConnectionStrings__Postgres: pgConfig.password.apply(
          (pwd) =>
            `postgres://${pgConfig.user}:${pwd}@${pgConfig.k8ResourceName}-postgresql.${defaultNamespace}.svc.cluster.local/${pgConfig.database}`
        ),
        ConnectionStrings__PostgresAdoFormat: pgConfig.password.apply(
          (pwd) =>
            `Server=${pgConfig.k8ResourceName}-postgresql.${defaultNamespace}.svc.cluster.local;Database=${pgConfig.database};Uid=${pgConfig.user};Pwd=${pwd}`
        )
      }
    },
    { provider }
  )
