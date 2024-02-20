import * as k8s from '@pulumi/kubernetes'

import { ports, isDev } from './environment'

export const initMockThirdPartyBankService = (
  provider: k8s.Provider
): k8s.core.v1.Service =>
  new k8s.core.v1.Service(
    'mockThirdPartyBank',
    {
      metadata: {
        name: 'mock-third-party-bank'
      },
      spec: {
        ports: [
          {
            port: ports.mockThirdPartyBank,
            protocol: 'TCP',
            targetPort: ports.mockThirdPartyBank
          }
        ],
        selector: {
          app: 'mock-third-party-bank'
        }
      }
    },
    { provider }
  )

export const initMockThirdPartyBankDeployment = (
  provider: k8s.Provider
): k8s.apps.v1.Deployment =>
  new k8s.apps.v1.Deployment(
    'mockThirdPartyBankDeployment',
    {
      metadata: {
        labels: {
          app: 'mock-third-party-bank'
        },
        name: 'mock-third-party-bank-deployment'
      },
      spec: {
        replicas: 1,
        selector: {
          matchLabels: {
            app: 'mock-third-party-bank'
          }
        },
        template: {
          metadata: {
            labels: {
              app: 'mock-third-party-bank'
            }
          },
          spec: {
            containers: [
              {
                env: [
                  {
                    name: 'TCP_BIND_PORT',
                    value: ports.mockThirdPartyBank.toString()
                  }
                ],
                image: isDev ? 'mock-third-party-bank:latest' : 'danne931/akka-dotnet-bank-mock-third-party-bank:latest',
                imagePullPolicy: isDev ? 'Never' : 'Always',
                livenessProbe: {
                  initialDelaySeconds: 10,
                  periodSeconds: 60,
                  tcpSocket: {
                    port: 'tcp-bind-port'
                  }
                },
                name: 'mock-third-party-bank',
                ports: [
                  {
                    containerPort: ports.mockThirdPartyBank,
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
