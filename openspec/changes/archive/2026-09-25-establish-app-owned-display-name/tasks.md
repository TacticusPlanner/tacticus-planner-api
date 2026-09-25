## 1. Profile and contract

- [x] 1.1 Treat an empty `Profile.DisplayName` as "no name set" with no schema change; first-access provisioning stores it empty instead of the provider claim; verify with API tests.
- [x] 1.2 Update first-access provisioning and `/me` mapping for a nullable name and a private suggestion; verify provider-name, email-like, and absent-claim API tests.
- [x] 1.3 Implement authenticated `PUT /me/display-name` with trim/length/control-character validation and a single atomic write; verify save/reload, failure atomicity, and cross-account isolation tests.

## 2. Import and public identity

- [x] 2.1 Return the submitted V1 username as `suggestedDisplayName` in the import response only after successful V1 login/profile retrieval and only while no name is set; verify V1 setup, failed login, and later re-import tests.
- [x] 2.2 Restrict UserJot `firstName` to the chosen name or fixed generic fallback; verify signed-token tests include no provider/email-like suggestion and reflect a fresh edit.
- [x] 2.3 Regenerate and inspect `artifacts/openapi` for `/me` and the new update endpoint, and coordinate its types with the paired apps change; verify a contract test or generated-client comparison.

## 3. Integration gates

- [ ] 3.1 Verify through Aspire with a new provider account, a V1 import, and an edited account that public feedback identity never exposes a provider or V1-derived name.
- [x] 3.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify all gates pass.
