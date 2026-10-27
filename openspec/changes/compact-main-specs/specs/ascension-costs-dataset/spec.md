## MODIFIED Requirements

### Requirement: The ascension ladder is built from the pinned datamine file

The served `ascension-costs` dataset SHALL be built from the datamine copy `gameconfig/units/heroProgressionSteps.json`, one record per row in file order. The catalog SHALL keep no hand-kept copy.

#### Scenario: Same ladder as before

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds the same 20 records the retired `Data/ascension-costs.json` served, and its manifest hash is unchanged

#### Scenario: The shipped ladder

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 20 records, the first `Common:None`
