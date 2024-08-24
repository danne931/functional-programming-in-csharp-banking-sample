import * as pulumi from '@pulumi/pulumi'
import * as k8s from '@pulumi/kubernetes'

import { ports, initContainers, isDev } from './environment'

const config = new pulumi.Config()

export const initWebCluster = (
  provider: k8s.Provider,
  bankEnvConfigMap: k8s.core.v1.ConfigMap
): k8s.apps.v1.StatefulSet =>
  new k8s.apps.v1.StatefulSet(
    'webCluster',
    {
      metadata: {
        labels: {
          app: 'web-cluster',
          cluster: 'akkabank'
        },
        name: 'web-cluster'
      },
      spec: {
        replicas: config.getNumber('webReplicas') ?? 1,
        selector: {
          matchLabels: {
            app: 'web-cluster'
          }
        },
        serviceName: 'web-service',
        template: {
          metadata: {
            labels: {
              app: 'web-cluster',
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
                    value: '$(POD_NAME).web-service'
                  },
                  {
                    name: 'DOTNET_URLS',
                    value: `http://+:${ports.webHttp}`
                  }
                ],
                envFrom: [
                  {
                    configMapRef: {
                      name: bankEnvConfigMap.metadata.name
                    }
                  }
                ],
                image: isDev ? 'web:latest' : 'danne931/akka-dotnet-bank-web:latest',
                imagePullPolicy: isDev ? 'Never' : 'Always',
                livenessProbe: {
                  initialDelaySeconds: 10,
                  tcpSocket: {
                    port: ports.akkaHealthCheckLiveness
                  }
                },
                name: 'web-cluster',
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
                  },
                  {
                    containerPort: ports.webHttp,
                    name: 'http',
                    protocol: 'TCP'
                  }
                ],
                readinessProbe: {
                  tcpSocket: {
                    port: 'http'
                  }
                }
              }
            ],
            initContainers: [initContainers.accountServiceReady, initContainers.readModelsReady],
            terminationGracePeriodSeconds: 35
          }
        }
      }
    },
    { provider }
  )

export const initWebService = (provider: k8s.Provider): k8s.core.v1.Service =>
  new k8s.core.v1.Service(
    'webService',
    {
      metadata: {
        labels: {
          app: 'web-cluster'
        },
        name: 'web-service'
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
          },
          {
            name: 'http',
            port: ports.webHttp
          }
        ],
        publishNotReadyAddresses: true,
        selector: {
          app: 'web-cluster'
        }
      }
    },
    { provider }
  )

export const initLocalWebLoadBalancer = (
  provider: k8s.Provider
): k8s.core.v1.Service =>
  new k8s.core.v1.Service(
    'webClusterHttp',
    {
      metadata: {
        labels: {
          app: 'web-cluster',
          tier: 'frontend'
        },
        name: 'web-cluster-http'
      },
      spec: {
        type: 'LoadBalancer',
        ports: [
          {
            nodePort: 30000,
            port: 3000,
            protocol: 'TCP',
            targetPort: ports.webHttp
          }
        ],
        selector: {
          app: 'web-cluster'
        }
      }
    },
    { provider }
  )

// TODO: https; host
export const initIngress = (provider: k8s.Provider): k8s.networking.v1.Ingress =>
  new k8s.networking.v1.Ingress('web-ingress', {
    metadata: {
      name: 'web-ingress'
    },
    spec: {
      ingressClassName: 'webapprouting.kubernetes.azure.com',
      rules: [{
        http: {
          paths: [{
            path: '/',
            pathType: 'Prefix',
            backend: {
              service: {
                name: 'web-service',
                port: {
                  number: ports.webHttp
                }
              }
            }
          }]
        }
      }]
    }
  }, { provider })
