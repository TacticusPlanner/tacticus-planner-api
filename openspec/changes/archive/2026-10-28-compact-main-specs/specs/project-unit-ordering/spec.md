## REMOVED Requirements

### Requirement: Flattened order remains the canonical scheduler order

**Reason**: D15 — covered by the account-wide in-flight order; its project-goal list constraint targets an endpoint `goal-collection-read` says does not exist.

**Migration**: See `global-goal-priority` — "Account has one canonical in-flight order" and `goal-collection-read` — "The projects list carries goal ids".

### Requirement: Goal creation does not accept numeric priority

**Reason**: D15 — superseded: creation now accepts `position`.

**Migration**: See `global-goal-priority` — "Lifecycle changes maintain global order".

### Requirement: Project goal order is addressable through a dedicated operation

**Reason**: D15 — covered by global reorder and in-project move.

**Migration**: See `global-goal-priority` — "Owner can reorder the complete global set" and "Owner can move a goal within a project's projection".

### Requirement: Membership replacement does not accept caller-authored priority

**Reason**: D15 — covered by membership save leaving global positions unchanged.

**Migration**: See `project-bulk-membership-replacement` — "Membership save does not reorder global goals" and `global-goal-priority` — "Lifecycle changes maintain global order".
