# k3s deployment from Jenkins (service account kubeconfig)

Jenkins runs on a local k8s agent and deploys to a remote Hetzner k3s cluster. Authentication uses a **kubeconfig file** containing a long-lived service account token, stored as a Jenkins **Secret File** credential.

---

## Prerequisites

- `kubectl` configured to reach the k3s cluster (e.g. via the k3s admin kubeconfig)
- The `deployment-manager` Role already exists in the `bf42-stats` namespace (from `deploy/app/deployment-manager.yaml`)

---

## 1. Create the ServiceAccount, token, and RoleBinding

```bash
kubectl apply -f deploy/k3s-jenkins-rbac.yaml
```

This creates:

| Resource | Name | Namespace |
|----------|------|-----------|
| ServiceAccount | `jenkins-deployer` | `bf42-stats` |
| Secret | `jenkins-deployer-token` | `bf42-stats` |
| RoleBinding | `jenkins-deployer-binding` | `bf42-stats` |

The RoleBinding grants the ServiceAccount the permissions defined in the existing `deployment-manager` Role.

---

## 2. Extract the token and CA certificate

```bash
# Service account token
TOKEN=$(kubectl -n bf42-stats get secret jenkins-deployer-token -o jsonpath='{.data.token}' | base64 -d)

# Cluster CA certificate (base64-encoded, for embedding in kubeconfig)
CA_DATA=$(kubectl -n bf42-stats get secret jenkins-deployer-token -o jsonpath='{.data.ca\.crt}')
```

---

## 3. Build the kubeconfig file

Replace `<K3S_API_SERVER>` with your k3s API server URL (e.g. `https://your-server:6443`):

```bash
cat > jenkins-kubeconfig.yaml <<EOF
apiVersion: v1
kind: Config
clusters:
  - name: k3s
    cluster:
      server: <K3S_API_SERVER>
      certificate-authority-data: ${CA_DATA}
contexts:
  - name: jenkins-deployer@k3s
    context:
      cluster: k3s
      namespace: bf42-stats
      user: jenkins-deployer
current-context: jenkins-deployer@k3s
users:
  - name: jenkins-deployer
    user:
      token: ${TOKEN}
EOF
```

---

## 4. Test the kubeconfig locally

```bash
kubectl --kubeconfig=jenkins-kubeconfig.yaml -n bf42-stats get pods
```

You should see the pods in the `bf42-stats` namespace. If this works, the kubeconfig is valid.

---

## 5. Add the kubeconfig to Jenkins

1. Go to **Jenkins > Manage Jenkins > Credentials**
2. Add a new **Secret file** credential
3. Upload `jenkins-kubeconfig.yaml`
4. Set the ID to `bf42-stats-k3s-kubeconfig`

The Jenkinsfile references this credential ID in the deploy stages.

---

## 6. Clean up old AKS credentials

Remove these Jenkins credentials (no longer needed):

| Credential ID | Reason |
|---------------|--------|
| `bf42-stats-aks-sp-client-id` | AKS service principal replaced by kubeconfig |
| `bf42-stats-aks-sp-client-secret` | AKS service principal replaced by kubeconfig |
| `bf42-stats-aks-sp-tenant-id` | AKS service principal replaced by kubeconfig |

---

## Jenkins credentials summary

| Credential ID | Type | Purpose |
|---------------|------|---------|
| `bf42-stats-k3s-kubeconfig` | Secret File | kubeconfig for k3s cluster |
| `bf42-stats-secrets-jwt-private-key` | Secret Text | JWT signing key |
| `bf42-stats-secrets-refresh-token-secret` | Secret Text | Refresh token secret |
| `jenkins-bf1942-stats-dockerhub-pat` | Username/Password | Docker Hub push |
