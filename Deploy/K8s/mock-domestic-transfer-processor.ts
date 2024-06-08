import * as k8s from '@pulumi/kubernetes'

import { ports, isDev } from './environment'

export const initMockDomesticTransferProcessorService = (
  provider: k8s.Provider
): k8s.core.v1.Service =>
  new k8s.core.v1.Service(
    'mockDomesticTransferProcessor',
    {
      metadata: {
        name: 'mock-domestic-transfer-processor'
      },
      spec: {
        ports: [
          {
            port: ports.mockDomesticTransferProcessor,
            protocol: 'TCP',
            targetPort: ports.mockDomesticTransferProcessor
          }
        ],
        selector: {
          app: 'mock-domestic-transfer-processor'
        }
      }
    },
    { provider }
  )

export const initMockDomesticTransferProcessorDeployment = (
  provider: k8s.Provider
): k8s.apps.v1.Deployment =>
  new k8s.apps.v1.Deployment(
    'mockDomesticTransferProcessorDeployment',
    {
      metadata: {
        labels: {
          app: 'mock-domestic-transfer-processor'
        },
        name: 'mock-domestic-transfer-processor-deployment'
      },
      spec: {
        replicas: 1,
        selector: {
          matchLabels: {
            app: 'mock-domestic-transfer-processor'
          }
        },
        template: {
          metadata: {
            labels: {
              app: 'mock-domestic-transfer-processor'
            }
          },
          spec: {
            containers: [
              {
                env: [
                  {
                    name: 'TCP_BIND_PORT',
                    value: ports.mockDomesticTransferProcessor.toString()
                  }
                ],
                image: isDev ? 'mock-domestic-transfer-processor:latest' : 'danne931/akka-dotnet-bank-mock-domestic-transfer-processor:latest',
                imagePullPolicy: isDev ? 'Never' : 'Always',
                livenessProbe: {
                  initialDelaySeconds: 10,
                  periodSeconds: 60,
                  tcpSocket: {
                    port: 'tcp-bind-port'
                  }
                },
                name: 'mock-domestic-transfer-processor',
                ports: [
                  {
                    containerPort: ports.mockDomesticTransferProcessor,
                    name: 'tcp-bind-port',
                    protocol: 'TCP'
                  }
                ]
              }
            ]
          }
        }
      }
    },
    { provider }
  )
