## 1. Catalog

- [x] 1.1 Make the raw week occurrence, served `startUtc` and served `definitionId` nullable
- [x] 1.2 Serve merchants without calendar weeks as undated events, from the latest dated event's month onwards
- [x] 1.3 Skip the calendar-week checks for undated events in validation
- [x] 1.4 Monday-first plan day order for an undated event

## 2. Tests

- [x] 2.1 Undated shared and per-week merchants, ordering and dropping of older undated events
- [x] 2.2 Served undated view has null start, definition and income
- [x] 2.3 Plan day order for an undated event; manifest snapshot schema hash
