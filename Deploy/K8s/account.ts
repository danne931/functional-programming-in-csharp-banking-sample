import * as pulumi from '@pulumi/pulumi'
import * as k8s from '@pulumi/kubernetes'

import { ports, initContainers, isDev } from './environment'

// Grab some values from the Pulumi stack configuration (or use defaults)
const config = new pulumi.Config()

// These environment variables can optionally be configured. Defaults
// are provided in ./Lib/Environment.fs and ./Transfer.App/Environment.fs.
const optionalEnv = [
  {
    name: 'DomesticTransferRouter__MaxInstancesPerNode',
    value: config.get('domesticTransferRouterMaxInstancesPerNode')
  },
  {
    name: 'DomesticTransferCircuitBreaker__MaxFailures',
    value: config.get('domesticTransferCircuitBreakerMaxFailures')
  },
  {
    name: 'DomesticTransferCircuitBreaker__CallTimeoutSeconds',
    value: config.get('domesticTransferCircuitBreakerCallTimeoutSeconds')
  },
  {
    name: 'DomesticTransferCircuitBreaker__ResetTimeoutSeconds',
    value: config.get('domesticTransferCircuitBreakerResetTimeoutSeconds')
  },
  {
    name: 'TransferProgressTrackingThrottle__Count',
    value: config.get('transferProgressTrackingThrottleCount')
  },
  {
    name: 'TransferProgressTrackingThrottle__Burst',
    value: config.get('transferProgressTrackingThrottleBurst')
  },
  {
    name: 'TransferProgressTrackingThrottle__Seconds',
    value: config.get('transferProgressTrackingThrottleSeconds')
  },
  {
    name: 'TransferProgressLookbackMinutes',
    value: config.get('transferProgressLookbackMinutes')
  },
  {
    name: 'BillingCycleFanoutThrottle__Count',
    value: config.get('billingCycleFanoutThrottleCount')
  },
  {
    name: 'BillingCycleFanoutThrottle__Burst',
    value: config.get('billingCycleFanoutThrottleBurst')
  },
  {
    name: 'BillingCycleFanoutThrottle__Seconds',
    value: config.get('billingCycleFanoutThrottleSeconds')
  },
  {
    name: 'AccountActorSupervisor__MinBackoffSeconds',
    value: config.get('accountActorSupervisorMinBackoffSeconds')
  },
  {
    name: 'AccountActorSupervisor__MaxBackoffSeconds',
    value: config.get('accountActorSupervisorMaxBackoffSeconds')
  },
  {
    name: 'AccountActorSupervisor__RandomFactor',
    value: config.get('accountActorSupervisorRandomFactor')
  },
  {
    name: 'AccountActorSupervisor__MaxNrOfRetries',
    value: config.get('accountActorSupervisorMaxNrOfRetries')
  },
  {
    name: 'AccountDeleteThrottle__Count',
    value: config.get('accountDeleteThrottleCount')
  },
  {
    name: 'AccountDeleteThrottle__Burst',
    value: config.get('accountDeleteThrottleBurst')
  },
  {
    name: 'AccountDeleteThrottle__Seconds',
    value: config.get('accountDeleteThrottleSeconds')
  },
  {
    name: 'AccountEventProjectionChunking__Size',
    value: config.get('accountEventProjectionChunkingSize')
  },
  {
    name: 'AccountEventProjectionChunking__Seconds',
    value: config.get('accountEventProjectionChunkingSeconds')
  },
  {
    name: 'AccountEventReadModelPersistenceBackoffRestart_MinBackoffSeconds',
    value: config.get('accountEventReadModelPersistenceBackoffRestartMinBackoffSeconds')
  },
  {
    name: 'AccountEventReadModelPersistenceBackoffRestart_MaxBackoffSeconds',
    value: config.get('accountEventReadModelPersistenceBackoffRestartMaxBackoffSeconds')
  },
  {
    name: 'AccountEventReadModelPersistenceBackoffRestart_RandomFactor',
    value: config.get('accountEventReadModelPersistenceBackoffRestartRandomFactor')
  },
  {
    name: 'AccountEventReadModelPersistenceBackoffRestart_MaxRestarts',
    value: config.get('accountEventReadModelPersistenceBackoffRestartMaxRestarts')
  },
  {
    name: 'AccountEventReadModelPersistenceBackoffRestart_MaxRestartsWithinSeconds',
    value: config.get('accountEventReadModelPersistenceBackoffRestartMaxRestartsWithinSeconds')
  },
  {
    name: 'BillingStatementPersistenceChunking__Size',
    value: config.get('billingStatementPersistenceChunkingSize')
  },
  {
    name: 'BillingStatementPersistenceChunking__Seconds',
    value: config.get('billingStatementPersistenceChunkingSeconds')
  },
  {
    name: 'BillingStatementPersistenceBackoffRestart_MinBackoffSeconds',
    value: config.get('billingStatementPersistenceBackoffRestartMinBackoffSeconds')
  },
  {
    name: 'BillingStatementPersistenceBackoffRestart_MaxBackoffSeconds',
    value: config.get('billingStatementPersistenceBackoffRestartMaxBackoffSeconds')
  },
  {
    name: 'BillingStatementPersistenceBackoffRestart_RandomFactor',
    value: config.get('billingStatementPersistenceBackoffRestartRandomFactor')
  },
  {
    name: 'BillingStatementPersistenceBackoffRestart_MaxRestarts',
    value: config.get('billingStatementPersistenceBackoffRestartMaxRestarts')
  },
  {
    name: 'BillingStatementPersistenceBackoffRestart_MaxRestartsWithinSeconds',
    value: config.get('billingStatementPersistenceBackoffRestartMaxRestartsWithinSeconds')
  },
  {
    name: 'CircuitBreakerActorSupervisor__MinBackoffSeconds',
    value: config.get('circuitBreakerActorSupervisorMinBackoffSeconds')
  },
  {
    name: 'CircuitBreakerActorSupervisor__MaxBackoffSeconds',
    value: config.get('circuitBreakerActorSupervisorMaxBackoffSeconds')
  },
  {
    name: 'CircuitBreakerActorSupervisor__RandomFactor',
    value: config.get('circuitBreakerActorSupervisorRandomFactor')
  },
  {
    name: 'CircuitBreakerActorSupervisor__MaxNrOfRetries',
    value: config.get('circuitBreakerActorSupervisorMaxNrOfRetries')
  },
  {
    name: 'CircuitBreakerActorSupervisor__ResetCounterAfterSeconds',
    value: config.get('circuitBreakerActorSupervisorResetCounterAfterSeconds')
  }
]

export const initAccountService = (
  provider: k8s.Provider
): k8s.core.v1.Service =>
  new k8s.core.v1.Service(
    'accountService',
    {
      metadata: {
        labels: {
          app: 'account-cluster'
        },
        name: 'account-service'
      },
      spec: {
        clusterIP: 'None',
        ports: [
          {
            name: 'akka-management',
            port: ports.akkaManagement
          },
          {
            name: 'akka-remote',
            port: ports.akkaRemoting
          }
        ],
        publishNotReadyAddresses: true,
        selector: {
          app: 'account-cluster'
        }
      }
    },
    { provider }
  )

export const initAccountCluster = (
  provider: k8s.Provider,
  bankEnvConfigMap: k8s.core.v1.ConfigMap
): k8s.apps.v1.StatefulSet =>
  new k8s.apps.v1.StatefulSet(
    'accountCluster',
    {
      metadata: {
        labels: {
          app: 'account-cluster',
          cluster: 'akkabank'
        },
        name: 'account-cluster'
      },
      spec: {
        replicas: config.getNumber('accountReplicas') ?? 4,
        selector: {
          matchLabels: {
            app: 'account-cluster'
          }
        },
        serviceName: 'account-service',
        template: {
          metadata: {
            labels: {
              app: 'account-cluster',
              cluster: 'akkabank'
            }
          },
          spec: {
            containers: [
              {
                env: [
                  {
                    name: 'POD_NAME',
                    valueFrom: {
                      fieldRef: {
                        fieldPath: 'metadata.name'
                      }
                    }
                  },
                  {
                    name: 'AkkaRemoting__Host',
                    value: '$(POD_NAME).account-service'
                  },
                  {
                    name: 'MockThirdPartyBank__Port',
                    value: ports.mockThirdPartyBank.toString()
                  },
                  {
                    name: 'EmailServiceUri',
                    value: 'https://api.useplunk.com/v1/'
                  },
                  ...optionalEnv.filter(o => o.value != null)
                ],
                envFrom: [
                  {
                    configMapRef: {
                      name: bankEnvConfigMap.metadata.name
                    }
                  }
                ],
                image: isDev ? 'account:latest' : 'danne931/akka-dotnet-bank-account:latest',
                imagePullPolicy: isDev ? 'Never' : 'Always',
                livenessProbe: {
                  initialDelaySeconds: 10,
                  tcpSocket: {
                    port: ports.akkaHealthCheckLiveness
                  }
                },
                name: 'account-cluster',
                ports: [
                  {
                    containerPort: ports.akkaManagement,
                    name: 'akka-management',
                    protocol: 'TCP'
                  },
                  {
                    containerPort: ports.akkaRemoting,
                    name: 'akka-remote',
                    protocol: 'TCP'
                  }
                ]
              }
            ],
            initContainers: [initContainers.postgresReady],
            terminationGracePeriodSeconds: 35
          }
        }
      }
    },
    { provider }
  )
