## REMOVED Requirements

### Requirement: Bulk transitions serialize with other goal mutations

**Reason**: D14 — status transitions are among the mutations serialized by the profile lock in `project-goal-slots`, including the order-change race and contiguous priorities.

**Migration**: See `project-goal-slots` — "Concurrent goal mutations of one profile serialize without failing".
