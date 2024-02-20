import * as k8s from '@pulumi/kubernetes'

export const initServiceAccount = (
  provider: k8s.Provider
): k8s.core.v1.ServiceAccount =>
  new k8s.core.v1.ServiceAccount(
    'akkabank',
    {
      metadata: {
        labels: {
          app: 'akkabank'
        },
        name: 'akkabank'
      }
    },
    { provider }
  )

interface AkkaRBAC {
  podReaderRole: k8s.rbac.v1.Role
  serviceAccountRoleBinding: k8s.rbac.v1.RoleBinding
  leaseAccessRole: k8s.rbac.v1.Role
}

export const initAkkaRBAC = (provider: k8s.Provider): AkkaRBAC => ({
  podReaderRole: new k8s.rbac.v1.Role(
    'podReader',
    {
      metadata: {
        name: 'pod-reader'
      },
      rules: [
        {
          apiGroups: [''],
          resources: ['pods'],
          verbs: ['get', 'watch', 'list']
        }
      ]
    },
    { provider }
  ),

  serviceAccountRoleBinding: new k8s.rbac.v1.RoleBinding(
    'readPods',
    {
      metadata: {
        name: 'read-pods'
      },
      roleRef: {
        apiGroup: 'rbac.authorization.k8s.io',
        kind: 'Role',
        name: 'pod-reader'
      },
      subjects: [
        {
          kind: 'ServiceAccount',
          name: 'default'
        }
      ]
    },
    { provider }
  ),

  leaseAccessRole: new k8s.rbac.v1.Role(
    'leaseAccess',
    {
      metadata: {
        name: 'lease-access'
      },
      rules: [
        {
          apiGroups: ['akka.io'],
          resources: ['leases'],
          verbs: ['get', 'create', 'update', 'list']
        }
      ]
    },
    { provider }
  )
})
