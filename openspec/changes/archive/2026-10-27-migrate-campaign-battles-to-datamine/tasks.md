# Tasks

Companion apps change: none (shapes and ids unchanged). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 6.

## 1. Binding

- [x] 1.1 Add `battles/campaigns.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineCampaigns` and the datamine and curation models
- [x] 1.3 Move group metadata, campaign ids and battle-id prefixes to `Data/curated/campaign-curation.json`
- [x] 1.4 Delete `Data/campaign-battles/`

## 2. Verification

- [x] 2.1 Compare old and new: same ids and order; differences limited to enemy power, tutorial enemy indices, I75's boss rank and DAS28
- [x] 2.2 Add `CampaignDatamineReaderTests`
- [x] 2.3 Promote the manifest snapshot (`campaign-battles`)
- [x] 2.4 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both copies)
