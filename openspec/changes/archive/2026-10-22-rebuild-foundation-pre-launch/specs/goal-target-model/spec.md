## REMOVED Requirements

### Requirement: Existing goals migrate to an equivalent acquisition-source set

**Reason**: The database is recreated with no data carried over; there are no persisted goals with the old `ascensionFarming` shape to rewrite.

**Migration**: None. The acquisition-source model ("Goal farming config carries an acquisition-source set") applies to every goal from creation.
