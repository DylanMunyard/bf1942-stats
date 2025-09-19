# BF1942 Stats API Ingress Setup

This document describes the ingress setup for the BF1942 Stats API using NGINX Ingress Controller on Azure Kubernetes Service (AKS).

## Installation

### 1. Install NGINX Ingress Controller
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.8.2/deploy/static/provider/cloud/deploy.yaml
```
