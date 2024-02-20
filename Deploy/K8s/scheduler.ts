import * as k8s from '@pulumi/kubernetes'

import { ports, initContainers, isDev } from './environment'

export const initSchedulerService = (
  provider: k8s.Provider
): k8s.core.v1.Service =>
  new k8s.core.v1.Service(
    'schedulerService',
    {
      metadata: {
        labels: {
          app: 'scheduler-cluster'
        },
        name: 'scheduler-service'
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
          app: 'scheduler-cluster'
        }
      }
    },
    { provider }
  )

export const initSchedulerCluster = (
  provider: k8s.Provider,
  bankEnvConfigMap: k8s.core.v1.ConfigMap
): k8s.apps.v1.StatefulSet =>
  new k8s.apps.v1.StatefulSet(
    'schedulerCluster',
    {
      metadata: {
        labels: {
          app: 'scheduler-cluster',
          cluster: 'akkabank'
        },
        name: 'scheduler-cluster'
      },
      spec: {
        replicas: 1,
        selector: {
          matchLabels: {
            app: 'scheduler-cluster'
          }
        },
        serviceName: 'scheduler-service',
        template: {
          metadata: {
            labels: {
              app: 'scheduler-cluster',
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
                    value: '$(POD_NAME).scheduler-service'
                  },
                  {
                    name: 'Quartz__SchedulerName',
                    value: 'Quartz Bank Scheduler'
                  },
                  {
                    name: 'Quartz__TablePrefix',
                    value: 'qrtz_'
                  }
                ],
                envFrom: [
                  {
                    configMapRef: {
                      name: bankEnvConfigMap.metadata.name
                    }
                  }
                ],
                image: isDev ? 'scheduler:latest' : 'danne931/akka-dotnet-bank-scheduler:latest',
                imagePullPolicy: isDev ? 'Never' : 'Always',
                livenessProbe: {
                  initialDelaySeconds: 10,
                  tcpSocket: {
                    port: ports.akkaHealthCheckLiveness
                  }
                },
                name: 'scheduler-cluster',
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
            initContainers: [initContainers.accountServiceReady, initContainers.accountReadModelsReady],
            terminationGracePeriodSeconds: 35
          }
        }
      }
    },
    { provider }
  )
