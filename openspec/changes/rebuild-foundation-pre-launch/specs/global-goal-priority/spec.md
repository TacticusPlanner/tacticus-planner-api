## REMOVED Requirements

### Requirement: Existing orders migrate deterministically

**Reason**: The database is recreated from a single initial migration with no data carried over; there is no existing order to migrate.

**Migration**: None. New profiles start with an empty in-flight order; "Lifecycle changes maintain global order" governs every position from then on.
