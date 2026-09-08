// One-off port of V1's datamined guild-boss data into the V2 game-catalog raw source file.
// Source of truth: tacticusplanner (develop) src/fsd/4-entities/guild_boss/data/guild_boss.json
// Output: src/TacticusPlanner.GameCatalog/Data/raid-bosses/raid-boss-data.json
//
// Re-run when refreshing to a new in-game version (see the game-catalog-data skill). This keeps only
// the fields the served `raid-bosses` projection needs; `misc`, `boards`, `visualId`, `spawnPointsSet`,
// `seasonEndRewards`, and other display/runtime-only fields are dropped.
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const src = resolve(here, "../../tacticusplanner/src/fsd/4-entities/guild_boss/data/guild_boss.json");
const outPath = resolve(here, "../src/TacticusPlanner.GameCatalog/Data/raid-bosses/raid-boss-data.json");

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

const out = {
  Rotation: raw.guildBossSeasonConfigRotation,
  Primarchs: raw.primarchs ?? [],
  UnitSets: unitSets,
  Seasons: seasons,
  Modifiers: modifiers,
};

mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, JSON.stringify(out, null, 2) + "\n", "utf8");

const counts = {
  unitSets: Object.keys(unitSets).length,
  seasons: Object.keys(seasons).length,
  modifiers: Object.keys(modifiers).length,
  encounters: Object.values(seasons).flatMap((s) => s.Tiers).flatMap((t) => t.Sets).flatMap((x) => x.Encounters).length,
  bytes: JSON.stringify(out).length,
};
console.log("wrote", outPath, counts);
