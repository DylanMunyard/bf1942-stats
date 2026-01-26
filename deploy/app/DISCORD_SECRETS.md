# discord-secrets (manual creation)

The `discord-secrets` Kubernetes secret is not defined in `deployment.yaml`. Create it manually before deploying, or the bf42-stats deployment will fail when resolving `DiscordOAuth__ClientId` and `DiscordOAuth__ClientSecret`.

## Create the secret

```bash
kubectl create secret generic discord-secrets \
  --namespace=bf42-stats \
  --from-literal=discord-client-id='123' \
  --from-literal=discord-client-secret='123' \
  --from-literal=suspicious-round-webhook='https://discord.com/api/webhooks/123'
```

Replace the example values with your real credentials:

| Key | Description |
|-----|-------------|
| `discord-client-id` | Discord OAuth application Client ID (Discord Developer Portal) |
| `discord-client-secret` | Discord OAuth application Client Secret |
| `suspicious-round-webhook` | Discord webhook URL for suspicious-round alerts (optional; omit or leave empty to disable) |

## Patch after creation

To update only one or more keys without recreating the secret:

```bash
kubectl patch secret discord-secrets -n bf42-stats -p '{"stringData":{"discord-client-id":"123","discord-client-secret":"123","suspicious-round-webhook":"https://discord.com/api/webhooks/123"}}'
```

Use `stringData` so values are plain strings; Kubernetes base64-encodes them.
