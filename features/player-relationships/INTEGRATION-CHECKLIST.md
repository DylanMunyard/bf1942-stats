# Neo4j Integration - Pre-Deployment Checklist

## Development Environment

### Infrastructure
- [ ] Neo4j container starts successfully
- [ ] Neo4j Browser accessible at http://localhost:7474
- [ ] Can login with `neo4j` / `bf1942stats`
- [ ] ~~Schema initialized~~ ✅ **Automatic via migrations!**
- [ ] ~~Test data visible~~ (No test data by default)

### API Configuration
- [ ] `Neo4j.Driver` NuGet package installed (v6.0.0+)
- [ ] `appsettings.Development.json` has Neo4j config (or set to empty to disable)
- [ ] API logs "Neo4j driver initialized" on startup
- [ ] API starts without Neo4j running (graceful degradation)

### Functionality
- [ ] Migrations run automatically on startup
- [ ] Migration status endpoint shows applied migrations
- [ ] Admin endpoint returns 400 when Neo4j disabled
- [ ] Admin endpoint requires Admin role (401/403 without)
- [ ] Sync returns success for valid request (1-365 days)
- [ ] Sync returns 400 for invalid days parameter
- [ ] Data appears in Neo4j after sync
- [ ] Re-sync is idempotent (no duplicates)
- [ ] Deleted sessions excluded from sync

### Performance
- [ ] Indexes created (run `SHOW INDEXES;` in Neo4j)
- [ ] Small sync (1 day) completes in <10 seconds
- [ ] Medium sync (30 days) completes in <2 minutes
- [ ] Queries run in <100ms with indexes
- [ ] Batch size is 1000 (check logs)

### Testing
- [ ] Test sync with 1 day (quick validation)
- [ ] Test sync with 7 days (realistic workload)
- [ ] Test sync with 30 days (larger dataset)
- [ ] Verify query results match expected data
- [ ] Test with no recent observations (should succeed with 0 relationships)

---

## Production Environment

### Infrastructure Setup
- [ ] Neo4j instance provisioned (Docker, K8s, or managed service)
- [ ] Neo4j version 5.x or higher
- [ ] Memory configured (recommended: 8GB heap + 12GB page cache for production)
- [ ] Persistent volumes configured
- [ ] Backups configured (Neo4j supports online backup)
- [ ] Monitoring configured (Neo4j metrics endpoint)

### Network & Security
- [ ] Neo4j accessible from API pods/containers
- [ ] Bolt port (7687) open between API and Neo4j
- [ ] HTTP port (7474) restricted to admin access only
- [ ] Strong password set (not default `bf1942stats`)
- [ ] TLS/SSL configured for Bolt connection (recommended)
- [ ] Network policies configured (if using K8s)

### Configuration
- [ ] Production `appsettings.json` has Neo4j config
- [ ] Connection string uses internal service name (e.g., `bolt://neo4j-service:7687`)
- [ ] Username/password stored in secrets (not plaintext config)
- [ ] Environment variables configured:
  - `Neo4j__Uri`
  - `Neo4j__Username`
  - `Neo4j__Password`
  - `Neo4j__Database`

### Schema Initialization
- [ ] ~~`scripts/init-neo4j.cypher` run~~ ✅ **Automatic via migrations!**
- [ ] All indexes created (verify with `SHOW INDEXES;`)
- [ ] Constraints created (verify with `SHOW CONSTRAINTS;`)
- [ ] Migration status shows all migrations applied
- [ ] No test data in production graph

### Initial Data Load
- [ ] Historical sync completed (recommend 90 days)
- [ ] Verify data count: `MATCH (n) RETURN labels(n), count(*)`
- [ ] Verify relationships: `MATCH ()-[r]->() RETURN type(r), count(*)`
- [ ] Sample queries tested
- [ ] Performance acceptable

### Ongoing Operations
- [ ] Background sync job scheduled (every 15 min, 1 day incremental)
- [ ] Monitoring alerts configured:
  - Sync failures
  - High query latency
  - Low disk space
  - Memory issues
- [ ] Logs aggregated (Seq, ELK, etc.)
- [ ] Backup schedule configured and tested
- [ ] Restore procedure documented

### Documentation
- [ ] Architecture documented
- [ ] Runbook created for common operations
- [ ] Troubleshooting guide updated
- [ ] Team trained on Neo4j Browser
- [ ] Query examples shared with team

---

## Deployment Steps

### Step 1: Deploy Neo4j
```bash
# Example: Kubernetes
kubectl apply -f neo4j-deployment.yaml

# Wait for ready
kubectl wait --for=condition=ready pod -l app=neo4j --timeout=300s
```

### Step 2: ~~Initialize Schema~~ (Automatic!)

✅ **Schema initialization is now automatic!** Migrations run when the API starts.

Just verify Neo4j is accessible:
```bash
kubectl port-forward svc/neo4j 7687:7687
# Test connection
echo "RETURN 1 AS test;" | cypher-shell -u neo4j -p $NEO4J_PASSWORD -a bolt://localhost:7687
```

### Step 3: Update API Configuration
```bash
# Set environment variables in deployment
kubectl set env deployment/api \
  Neo4j__Uri=bolt://neo4j-service:7687 \
  Neo4j__Username=neo4j \
  Neo4j__Database=neo4j

# Set password from secret
kubectl create secret generic neo4j-credentials \
  --from-literal=password=your-secure-password

# Update deployment to use secret
# (add to deployment.yaml)
env:
  - name: Neo4j__Password
    valueFrom:
      secretKeyRef:
        name: neo4j-credentials
        key: password
```

### Step 4: Deploy API
```bash
kubectl apply -f api-deployment.yaml

# Check logs for migrations
kubectl logs -l app=api --tail=100 | grep -A5 "Neo4j"

# Should see:
# Neo4j driver initialized: bolt://neo4j-service:7687
# Checking for pending Neo4j migrations...
# All migrations applied successfully
```

### Step 5: Initial Sync
```bash
# Get admin token
TOKEN=$(curl -s -X POST https://your-api/stats/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"..."}' \
  | jq -r '.accessToken')

# Sync last 90 days
curl -X POST https://your-api/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 90}'
```

### Step 6: Verify
```bash
# Check Neo4j Browser or run queries via API
# Verify data count, query performance

# Monitor for a few hours
kubectl logs -l app=api -f | grep -i neo4j
```

### Step 7: Schedule Background Job
```bash
# Add to scheduled job configuration (CronJob, Hangfire, etc.)
# Every 15 minutes: sync last 1 day
```

---

## Rollback Plan

### If Neo4j Fails to Start
1. Check logs: `kubectl logs -l app=neo4j`
2. Verify persistent volume claims
3. Check memory/CPU limits
4. If needed, scale down to 0 and redeploy

### If API Can't Connect
1. Verify service names and ports
2. Check network policies
3. Verify secrets are mounted
4. Temporarily disable Neo4j (set `Neo4j__Uri=""`)

### If Sync Fails
1. API continues to work (Neo4j is optional)
2. Check Neo4j logs for errors
3. Verify indexes exist
4. Check memory usage (may need more RAM)
5. Reduce sync window (fewer days)

### If Queries are Slow
1. Verify indexes: `SHOW INDEXES;`
2. Run `scripts/init-neo4j.cypher` if missing
3. Increase Neo4j heap/page cache
4. Check query with `PROFILE`

### Emergency Disable
```bash
# Set Neo4j__Uri to empty string
kubectl set env deployment/api Neo4j__Uri=""

# Sync endpoint will return friendly error
# Existing API functionality unaffected
```

---

## Success Metrics

After deployment, verify:

- ✅ **Uptime:** Neo4j container running without restarts
- ✅ **Connectivity:** API can connect to Neo4j
- ✅ **Data:** Player and Server nodes exist
- ✅ **Relationships:** PLAYED_WITH and PLAYS_ON edges exist
- ✅ **Performance:** Queries return in <100ms (95th percentile)
- ✅ **Sync:** Background job runs successfully every 15 minutes
- ✅ **No Errors:** No Neo4j-related errors in API logs
- ✅ **Memory:** Neo4j memory usage stable (no growth)
- ✅ **Disk:** Neo4j disk usage growing linearly with data

---

## Contact

For issues or questions:
- Check `features/player-relationships/TROUBLESHOOTING.md` (if created)
- Review logs in Seq/ELK
- Check Neo4j Browser for query debugging
- Neo4j Community Forum: https://community.neo4j.com/
