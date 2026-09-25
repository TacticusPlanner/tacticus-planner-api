## 1. Target identity and database

- [x] 1.1 Add one normalized Rank end-target key shared by validation, membership, and V1 import; verify equivalence/inequivalence tests for clean, point-five, and applied-slot targets.
- [x] 1.2 Add the key to project membership and create an EF migration with `dotnet ef migrations add ...` to backfill Rank rows and replace the old partial index; verify PostgreSQL migration and relational uniqueness tests.

## 2. All mutation paths

- [x] 2.1 Update single/combined creation and goal/project membership replacement to allow distinct Rank keys but reject exact duplicates atomically; verify endpoint tests for same/different targets and multi-project partial conflict.
- [x] 2.2 Update status transitions, concurrent creation, and conflict translation for Rank target slots while preserving non-Rank rules; verify race tests yield one 409 for equal targets and both successes for distinct targets.
- [x] 2.3 Verify hard delete releases every membership slot and recreation has a new id with no stale state; add API/domain regression tests including a completed historical milestone.

## 3. Import and handoff

- [x] 3.1 Preserve distinct V1 Rank targets and their order/notes while merging exact duplicates; verify import tests for two targets, equivalent duplicates, and non-Rank merging.
- [ ] 3.2 Regenerate and inspect `artifacts/openapi` for conflict response changes and coordinate with paired apps change; verify the generated schema and consumer types match.
- [x] 3.3 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify all gates pass.
