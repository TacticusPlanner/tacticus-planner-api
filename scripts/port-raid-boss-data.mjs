// One-off port of V1's datamined guild-boss data into the V2 game-catalog raw source files.
// Source of truth: tacticusplanner (develop) src/fsd/4-entities/guild_boss/data/guild_boss.json
// Output: src/TacticusPlanner.GameCatalog/Data/raid-bosses/
//   raid-boss-common.json             - season-config rotation, primarch prime ids, modifier definitions
//   raid-boss-<n>-<Type>.json  (x N)  - one file per raid boss: the unit sets keyed GuildBoss<n>...;
//                                       <Type> is the boss's Boss1 key minus the GuildBoss<n>Boss1 prefix
//   raid-boss-season-<n>.json  (x M)  - one file per guild_boss_season_config_<n>
//
// The loader (GameCatalogLoader.LoadRaidBossRawData) merges these back into one GameCatalogRaidBossRawData
// and denormalizes to the single served `raid-bosses` dataset; no raw file is served directly.
//
// Re-run when refreshing to a new in-game version (see the game-catalog-data skill). This keeps only
// the fields the served `raid-bosses` projection needs; `misc`, `boards`, `visualId`, `spawnPointsSet`,
// `seasonEndRewards`, and other display/runtime-only fields are dropped.
import { readFileSync, writeFileSync, mkdirSync, readdirSync, rmSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const src = resolve(here, "../../tacticusplanner/src/fsd/4-entities/guild_boss/data/guild_boss.json");
const outDir = resolve(here, "../src/TacticusPlanner.GameCatalog/Data/raid-bosses");

const raw = JSON.parse(readFileSync(src, "utf8"));

const pick = (obj, keys) => {
  const out = {};
  for (const k of keys) if (obj[k] !== undefined) out[k] = obj[k];
  return out;
};

const STAT_KEYS = [
  "Health", "Damage", "FixedArmor", "Rank", "StarLevel", "BaseRarity",
  "ProgressionIndex", "AbilityLevel", "RelicAbilityLevel",
  "BlockChance", "BlockDamage", "CritChance", "CritDamage",
];

const unitSets = {};
for (const [id, u] of Object.entries(raw.unitSets)) {
  unitSets[id] = {
    FactionId: u.FactionId,
    Movement: u.Movement ?? 0,
    ...(u.nrOfMembers !== undefined ? { NrOfMembers: u.nrOfMembers } : {}),
    ...(u.questUnitId !== undefined ? { QuestUnitId: u.questUnitId } : {}),
    Stats: (u.stats ?? []).map((s) => pick(s, STAT_KEYS)),
    ...(u.weapons ? { Weapons: u.weapons.map((w) => pick(w, ["hits", "DamageProfile", "Range"])) } : {}),
    ...(u.activeAbilities ? { ActiveAbilities: u.activeAbilities } : {}),
    ...(u.passiveAbilities ? { PassiveAbilities: u.passiveAbilities } : {}),
    ...(u.relicAbilities ? { RelicAbilities: u.relicAbilities } : {}),
    ...(u.traits ? { Traits: u.traits } : {}),
  };
}

const seasons = {};
for (const [id, s] of Object.entries(raw.guildBossSeasonDataConfigsGDTO)) {
  seasons[id] = {
    GuildBossSeasonConfigId: s.guildBossSeasonConfigId,
    ...(s.loopFromTier !== undefined ? { LoopFromTier: s.loopFromTier } : {}),
    ...(s.loopFromSet !== undefined ? { LoopFromSet: s.loopFromSet } : {}),
    Tiers: s.tiers.map((t) => ({
      Tier: t.tier,
      Sets: t.sets.map((set) => ({
        Set: set.set,
        ChestId: set.chestId,
        GuildXp: set.guildXp ?? 0,
        Encounters: set.encounters.map((e) => ({
          EncounterIndex: e.encounterIndex,
          GuildBossEncounterType: e.guildBossEncounterType,
          BoardId: e.boardId,
          MaxNrOfTurns: e.maxNrOfTurns ?? 0,
          ...(e.bossType !== undefined ? { BossType: e.bossType } : {}),
          UnitId: e.unitId,
          ...(e.npc1id !== undefined ? { Npc1Id: e.npc1id } : {}),
          ...(e.npc2id !== undefined ? { Npc2Id: e.npc2id } : {}),
          ...(e.enemies ? { Enemies: e.enemies } : {}),
          ...(e.disallowedFactions ? { DisallowedFactions: e.disallowedFactions } : {}),
          ...(e.modifiers ? { Modifiers: e.modifiers.map((m) => ({ HpLost: m.hpLost, Modifier: m.modifier })) } : {}),
        })),
      })),
    })),
  };
}

const modifiers = {};
for (const [id, m] of Object.entries(raw.modifiers)) {
  modifiers[id] = pick(m, ["type", "target", "subtarget", "amount"]);
}

// ---- split into authored files -------------------------------------------------------------------
mkdirSync(outDir, { recursive: true });

// wipe any previous raid-boss-*.json so a boss/season removed upstream does not linger
for (const name of readdirSync(outDir)) {
  if (/^raid-boss-.*\.json$/.test(name)) rmSync(resolve(outDir, name));
}

const write = (name, value) =>
  writeFileSync(resolve(outDir, name), JSON.stringify(value, null, 2) + "\n", "utf8");

write("raid-boss-common.json", {
  Rotation: raw.guildBossSeasonConfigRotation,
  Primarchs: raw.primarchs ?? [],
  Modifiers: modifiers,
});

const bossFiles = {};
for (const [key, unitSet] of Object.entries(unitSets)) {
  const m = /^GuildBoss(\d+)/.exec(key);
  if (!m) throw new Error(`unit-set key does not start with GuildBoss<n>: ${key}`);
  (bossFiles[Number(m[1])] ??= {})[key] = unitSet;
}
const bossNums = Object.keys(bossFiles).map(Number).sort((a, b) => a - b);
for (const n of bossNums) {
  const keys = Object.keys(bossFiles[n]);
  const primary = keys.find((k) => new RegExp(`^GuildBoss${n}Boss1`).test(k));
  if (!primary) throw new Error(`raid boss ${n} has no GuildBoss${n}Boss1 unit set`);
  const type = primary.replace(new RegExp(`^GuildBoss${n}Boss1`), "");
  write(`raid-boss-${n}-${type}.json`, { UnitSets: bossFiles[n] });
}

const seasonNums = [];
for (const [key, season] of Object.entries(seasons)) {
  const m = /_(\d+)$/.exec(key);
  if (!m) throw new Error(`season key has no trailing number: ${key}`);
  seasonNums.push(Number(m[1]));
  write(`raid-boss-season-${m[1]}.json`, season);
}
seasonNums.sort((a, b) => a - b);

console.log("wrote", outDir, {
  bosses: bossNums.length,
  seasons: seasonNums.length,
  unitSets: Object.keys(unitSets).length,
  modifiers: Object.keys(modifiers).length,
  encounters: Object.values(seasons)
    .flatMap((s) => s.Tiers)
    .flatMap((t) => t.Sets)
    .flatMap((x) => x.Encounters).length,
});
