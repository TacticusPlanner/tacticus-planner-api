## 1. Hangfire foundation

- [x] 1.1 Hangfire with PostgreSQL storage in the `hangfire` schema, its own pool derived from `planner-db`
- [x] 1.2 One server on the `default` queue, off via `BackgroundJobs:ServerEnabled` and during OpenAPI generation
- [x] 1.3 Test hosts enqueue into real storage without a server
- [x] 1.4 README configuration table and "Background jobs" section, AGENTS.md

## 2. Entra deletion retry

- [x] 2.1 `ExternalIdentityDeletionJob` throws on failure so Hangfire retries it (8 attempts, then failed)
- [x] 2.2 `PurgeAccountEndpoint` enqueues it when the inline deletion fails, the oid is a GUID and Graph is configured
- [x] 2.3 Unit tests for the job; integration tests for enqueueing and for a server run that completes the deletion

## Deferred / out-of-session

- [ ] 3.1 Apply the infra change (prod max 2 replicas) before this deploys
- [ ] 3.2 After the prod deploy, confirm the `hangfire` schema exists and the server started in the logs
