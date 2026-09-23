## 1. Profile and contract

- [ ] 1.1 Add `DisplayNameConfirmedAt` to `Profile` and create its EF migration with `dotnet ef migrations add ...`; verify existing-name rows remain intact and unconfirmed in a migration integration test.
- [ ] 1.2 Update first-access provisioning and `/me` mapping for nullable confirmed name, private suggestion, and confirmation state; verify provider-name, email-like, absent, and legacy-profile API tests.
- [ ] 1.3 Implement authenticated `PUT /me/display-name` with trim/length/control-character validation and atomic confirmation; verify save/reload, failure atomicity, and cross-account isolation tests.

## 2. Import and public identity

- [ ] 2.1 Seed an unconfirmed suggestion from the submitted username only after successful V1 login/profile retrieval, preserving confirmed names; verify normal setup, V1 setup, failed login, and later re-import tests.
- [ ] 2.2 Restrict UserJot `firstName` to a confirmed name or fixed generic fallback; verify signed-token tests include no provider/email-like suggestion and reflect a fresh edit.
- [ ] 2.3 Regenerate and inspect `artifacts/openapi` for `/me` and the new update endpoint, and coordinate its types with the paired apps change; verify a contract test or generated-client comparison.

## 3. Integration gates

- [ ] 3.1 Verify through Aspire with a new provider account, a legacy unconfirmed account, a V1 import, and an edited account that public feedback identity never exposes an unconfirmed name.
- [ ] 3.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify all gates pass.
