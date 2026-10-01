## 1. Eligibility

- [x] 1.1 Extend `IsUnlockEligible` to also accept a regular shop shard variant for the character; reword the rejection message to name both sources
- [x] 1.2 Unit test the lookup: campaign-only, shop-only, mythic-only shop, neither

## 2. Endpoint behaviour

- [x] 2.1 Add create-goal endpoint tests: shop-only Unlock accepted (with a `Shop` acquisition source), mythic-only rejected, no-source rejected, owned shop-only rejected

## 3. Verify

- [x] 3.1 Run the API unit and endpoint test projects and the formatter/analyzers per AGENTS.md
