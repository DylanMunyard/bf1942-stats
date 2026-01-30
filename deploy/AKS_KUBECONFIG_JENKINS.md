# AKS deployment from Jenkins (service principal setup)

Jenkins runs on a local k8s agent and deploys to the remote AKS cluster. When AKS has **local accounts disabled**, you cannot use `az aks get-credentials --admin`. Use a **service principal** so the pipeline can authenticate without SSO and obtain a kubeconfig inside the job.

This guide covers: creating the service principal, granting Azure and Kubernetes permissions, and configuring Jenkins.

---

## Prerequisites

- Azure CLI, logged in as a user that can create service principals and manage the AKS cluster
- AKS cluster with local accounts disabled (or you choose to use an SP anyway)
- Jenkins job that uses the pipeline parameters and credentials below

---

## 1. Create the service principal

```bash
az ad sp create-for-rbac --name "sp-jenkins-bf42-stats-aks" --skip-assignment
```

From the output, note:

- **appId** — use as Jenkins credential `bf42-stats-aks-sp-client-id`
- **password** — use as Jenkins credential `bf42-stats-aks-sp-client-secret`
- **tenant** — use as Jenkins credential `bf42-stats-aks-sp-tenant-id`

---

## 2. Grant Azure access to the cluster

The SP must have permission to obtain cluster credentials. Get your subscription ID:

```bash
az account show --query id -o tsv
```

Assign the **Azure Kubernetes Service Cluster User Role** to the SP (replace placeholders):

```bash
az role assignment create \
  --assignee <appId-from-step-1> \
  --role "Azure Kubernetes Service Cluster User Role" \
  --scope /subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.ContainerService/managedClusters/<cluster-name>
```

- **resource-group** — AKS resource group (e.g. `bfstats-io`)
- **cluster-name** — AKS cluster name (e.g. `bfstats-aks`)

This allows the SP to run `az aks get-credentials` and receive a kubeconfig. It does **not** grant permissions inside the cluster; that is step 3.

---

## 3. Grant Kubernetes RBAC in the target namespace

AKS presents the SP to the API server as a **User** whose name is the service principal’s **object ID** (not the appId). Grant that identity a Role in the namespace where Jenkins deploys (e.g. `bf42-stats`).

Get the SP’s object ID:

```bash
az ad sp show --id <appId-from-step-1> --query id -o tsv
```

Apply the Role and RoleBinding. Either:

- Use the provided manifest and substitute the object ID, or  
- Use the existing file if it already has the correct object ID.

**Option A — Use the existing manifest**

`deploy/aks-jenkins-rbac.yaml` defines a Role `jenkins-deployer` in namespace `bf42-stats` (secrets, pods, deployments) and a RoleBinding to a User name (the SP object ID). If you created the SP in this repo’s setup, the file may already contain the right object ID. Otherwise edit the `subjects[0].name` value to the object ID from the command above.

Apply with a kubeconfig that has admin rights (e.g. your user):

```bash
az login
az aks get-credentials --resource-group <resource-group> --name <cluster-name>
kubectl apply -f deploy/aks-jenkins-rbac.yaml
```

**Option B — Create your own Role/RoleBinding**

Bind a Role in `bf42-stats` to the User name equal to the SP’s **object ID**. The Role must allow at least: `secrets` (create, get, list, update, patch), `deployments` (get, list, patch). See `deploy/aks-jenkins-rbac.yaml` for a full example.

---

## 4. Jenkins credentials

Add **Secret text** credentials in Jenkins with these exact IDs (the Jenkinsfile references them):

| Credential ID | Secret value |
|---------------|--------------|
| `bf42-stats-aks-sp-client-id` | Service principal **appId** |
| `bf42-stats-aks-sp-client-secret` | Service principal **password** |
| `bf42-stats-aks-sp-tenant-id` | Azure **tenant** ID |

---

## 5. Jenkins pipeline parameters

The pipeline needs the AKS resource group and cluster name. Configure the job parameters (e.g. in **Build with Parameters** or **Configure → Parameters**):

| Parameter | Example | Description |
|-----------|---------|-------------|
| `AKS_RESOURCE_GROUP` | `bfstats-io` | AKS resource group name |
| `AKS_CLUSTER_NAME` | `bfstats-aks` | AKS cluster name |

Set default values so normal builds don’t require typing them.

---

## 6. Deploy container image (Azure CLI + kubectl)

The deploy stages use the **deploy-aks** container (see `deploy/pod.yaml`), which must provide both **Azure CLI** and **kubectl**. Build and push the image from the repo:

```bash
# From repo root. Jenkins deploy agent is amd64.
docker build -f deploy/Dockerfile.jenkins-aks-deploy -t dylanmunyard/jenkins-agent-kubectl-az:latest deploy/
docker push dylanmunyard/jenkins-agent-kubectl-az:latest
```

The Dockerfile (`deploy/Dockerfile.jenkins-aks-deploy`) is based on the official Azure CLI image and installs kubectl. If you use a different registry or tag, update the `deploy-aks` container image in `deploy/pod.yaml`.

---

## Verify

Using the SP locally (no browser):

```bash
az login --service-principal -u <appId> -p '<password>' --tenant '<tenant-id>'
az aks get-credentials --resource-group <resource-group> --name <cluster-name>
kubectl -n bf42-stats get pods
```

If that succeeds, the Jenkins pipeline should have the same access.

---

## If local accounts are enabled

If your cluster allows local accounts, you can use the admin kubeconfig instead of an SP:

```bash
az aks get-credentials --resource-group <rg> --name <cluster> --admin
```

Upload that kubeconfig as a **Secret file** in Jenkins. The current Jenkinsfile is written for SP-only; to use a static kubeconfig you would need to change the Deploy stages to use that file and skip `az login` / `az aks get-credentials`.
