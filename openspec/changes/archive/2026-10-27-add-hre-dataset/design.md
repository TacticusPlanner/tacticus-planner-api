# Design

## Context

The game config at `v1.43.157.2989` holds two Hero Release Events (Weirdboy, Eradicator). Each names its hero, quest
chain start (`questId`) and scoring; the leaderboards and the chest shop are separate files. The apps' tables came from
the wiki and match the game: the milestones sum to 6090 currency, the 20 paid missions to 2000, the first phase chest
is 550 currency and 33 shards, and the chest shop runs 100 to 5000 currency. Companion apps change: roadmap step 7
"Use `hre`".

## Decisions

### D1. One self-contained record per event

Like `lres`, each event carries everything the planner needs, so the client never joins. The chest shop is the same
for every event and is repeated per event; it is small.

### D2. Game task names, not planner objective kinds

Missions serve the game's task `name`, `target` and `parameters` (`factionTrait`, `factionId`, `gameMode`, `heroId`,
`damageProfileType`). Mapping them to the planner's objective kinds and text is presentation, so it stays in the apps,
like mythic quest text.

### D3. Leaderboard link by event number

The event config does not name its leaderboard. Event N reads `heroReleaseLeaderboard` (N = 1) or
`heroReleaseLeaderboard_N`, matching the two files shipped today (they are identical). A missing leaderboard fails
startup, so a renamed key is caught on the next datamine bump.

### D4. Brackets by starting position

The game keys each bracket by its first 0-based position (0 is first place). The dataset keeps that; the apps' "up to"
form is a client derivation.

### D5. Milestones not cumulated

Each milestone serves the currency it pays, as the game does. The apps cumulate.
