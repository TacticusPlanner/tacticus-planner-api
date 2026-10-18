using System.Globalization;
using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    public static IReadOnlyList<GameCatalogLreView> BuildLres(
        IReadOnlyDictionary<string, GameCatalogLre> lresByEvent,
        IReadOnlyDictionary<string, GameCatalogFactionUnits> unitsByFaction)
    {
        var roster = unitsByFaction
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .SelectMany(faction => faction.Characters.Select(character =>
                (character.Id, Faction: faction.FactionId, faction.Alliance)))
            .ToArray();

        return lresByEvent
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .Select(lre => new GameCatalogLreView(
                lre.UnitSnowprintId,
                lre.Name,
                BuildEventStageStartDates(lre.RunStartDatesUtc),
                lre.BattlesCount,
                lre.ConstraintsCount,
                BuildLreMissions(lre.RegularMissions),
                BuildLreMissions(lre.PremiumMissions),
                BuildTrackView(lre.UnitSnowprintId, "alpha", lre.Alpha, roster),
                BuildTrackView(lre.UnitSnowprintId, "beta", lre.Beta, roster),
                BuildTrackView(lre.UnitSnowprintId, "gamma", lre.Gamma, roster),
                new GameCatalogLreRewards(lre.PointsMilestones, lre.ChestsMilestones, lre.Progression, lre.ShardsPerChest)))
            .ToArray();
    }

    private static readonly (string Key, Func<GameCatalogLre, GameCatalogLreTrack> Track)[] LreTrackSelectors =
    [
        ("alpha", lre => lre.Alpha),
        ("beta", lre => lre.Beta),
        ("gamma", lre => lre.Gamma),
    ];

    private static string LreBattleId(string lreId, string track, int number) => $"{lreId}-{track}-{number}";

    // lre-battles: every battle flattened across all events × tracks, each carrying its lreId + track and a
    // composite id that the owning track's BattleIds resolve to.
    public static IReadOnlyList<GameCatalogLreBattleView> BuildLreBattles(
        IReadOnlyDictionary<string, GameCatalogLre> lresByEvent) =>
        lresByEvent
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .SelectMany(lre => LreTrackSelectors.SelectMany(selector =>
                selector.Track(lre).Battles.Select(battle => new GameCatalogLreBattleView(
                    LreBattleId(lre.UnitSnowprintId, selector.Key, battle.Number),
                    lre.UnitSnowprintId,
                    selector.Key,
                    battle.MapId,
                    battle.Number,
                    battle.Power,
                    battle.Tier,
                    battle.DisallowedFactions,
                    battle.Waves))))
            .ToArray();

    // The raw file lists the announced run starts in run order (e.g. "Sun, 08 March 2026 00:00:00 GMT").
    // Normalise each to ISO 8601 UTC, keeping the order, so the served index is the run number minus one.
    // An unparseable entry is passed through unchanged; validation (ValidateLreRunStarts, which runs after
    // denormalization) rejects it, so it never reaches a served payload.
    private static string[] BuildEventStageStartDates(IReadOnlyList<string> runStartDatesUtc) =>
        runStartDatesUtc
            .Select(start => ParseLreRunStart(start)?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                ?? start)
            .ToArray();

    // Accepted raw run start formats: RFC 1123 with an abbreviated or full month name, and ISO 8601 in UTC.
    // Exact formats keep partial values such as "8 March" or "12:00" from filling in from the current clock.
    private static readonly string[] LreRunStartFormats =
    [
        "r",
        "ddd, dd MMMM yyyy HH:mm:ss 'GMT'",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];

    // Parses one raw run start as a UTC instant; null when it is not a full date in an accepted format.
    internal static DateTimeOffset? ParseLreRunStart(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && DateTimeOffset.TryParseExact(value, LreRunStartFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static GameCatalogLreTrackView BuildTrackView(
        string lreId,
        string trackKey,
        GameCatalogLreTrack track,
        IReadOnlyList<(string Id, string Faction, string Alliance)> roster)
    {
        var availableUnitIds = roster
            .Where(unit => track.AllowedUnitsFilter.All(filter => Passes(filter, unit.Faction, unit.Alliance)))
            .Select(unit => unit.Id)
            .ToArray();

        return new GameCatalogLreTrackView(
            track.Name,
            track.Enemies,
            track.KillPoints,
            track.BattlesPoints,
            track.DefeatAll,
            track.AllowedUnitsFilter,
            track.UnitsRestrictions,
            track.Battles.Select(battle => LreBattleId(lreId, trackKey, battle.Number)).ToArray(),
            availableUnitIds);
    }

    private static bool Passes(GameCatalogLreFilter filter, string faction, string alliance)
    {
        var matches = filter.Kind switch
        {
            "Alliance" => string.Equals(alliance, filter.Target, StringComparison.Ordinal),
            "Faction" => string.Equals(faction, filter.Target, StringComparison.Ordinal),
            _ => false,
        };

        return filter.Exclude ? !matches : matches;
    }

    // ---- missions ------------------------------------------------------------------------------------------
    // Raw files keep V1's English mission lines (V1's questsToMissionText output) so a refresh from the V1
    // datamine stays a copy. Each line is matched against LreMissionTemplates in order; the first match names the
    // kind, its count (group "count") and at most one label (group "label") that the template's alias table maps
    // to a catalog id. A line no template matches, or a label with no alias, is served as an Unparsed placeholder;
    // ValidateLreMissions (which runs after denormalization) re-parses the raw lines and fails the load, so a
    // placeholder never reaches a served payload.

    /// <summary>Kind of the placeholder a line that fails to parse denormalizes to; never served (the load fails).</summary>
    internal const string UnparsedLreMissionKind = "unparsed";

    private static GameCatalogLreMission[] BuildLreMissions(IReadOnlyList<string> lines) =>
        lines
            .Select((text, index) => ParseLreMission(index, text, out _)
                ?? new GameCatalogLreMission(index, UnparsedLreMissionKind, 0, null, null, null, null, text))
            .ToArray();

    /// <summary>Which typed parameter a template's label group fills.</summary>
    internal enum LreMissionLabel
    {
        None,
        Faction,
        Alliance,
        DamageType,
        Trait,
    }

    internal sealed record LreMissionTemplate(Regex Pattern, string Kind, LreMissionLabel Label);

    // Ordered: the first template whose pattern matches the whole line wins. The "Defeat N <label>" fallback is
    // last so every more specific "Defeat N ..." line is claimed first.
    internal static readonly LreMissionTemplate[] LreMissionTemplates =
    [
        new(AnyBattleMissionPattern(), LreMissionKinds.AnyBattle, LreMissionLabel.None),
        new(DamageTypeMissionPattern(), LreMissionKinds.DamageType, LreMissionLabel.DamageType),
        new(UseAbilitiesWithAllianceMissionPattern(), LreMissionKinds.UseAbilitiesWithAlliance, LreMissionLabel.Alliance),
        new(DefeatWithAllianceMissionPattern(), LreMissionKinds.DefeatWithAlliance, LreMissionLabel.Alliance),
        new(DefeatWithDamageTypeMissionPattern(), LreMissionKinds.DefeatWithDamageType, LreMissionLabel.DamageType),
        new(WinWithoutSummonMissionPattern(), LreMissionKinds.WinWithoutSummon, LreMissionLabel.None),
        new(WinWithoutTraitMissionPattern(), LreMissionKinds.WinWithoutTrait, LreMissionLabel.Trait),
        // "Defeat 30 Daemon enemies" (V1's SlayEnemiesOfTrait): a trait-defined enemy group, served as
        // defeatFaction with Trait set and Faction null; the client matches it by npc trait.
        new(DefeatTraitEnemiesMissionPattern(), LreMissionKinds.DefeatFaction, LreMissionLabel.Trait),
        new(DefeatFactionMissionPattern(), LreMissionKinds.DefeatFaction, LreMissionLabel.Faction),
    ];

    // "Defeat 5 waves of enemies in any wave-based game mode, Deal 1000 damage"
    [GeneratedRegex(@"^Defeat (?<count>\d+) waves of enemies in any wave-based game mode, Deal \d+ damage$", RegexOptions.CultureInvariant)]
    private static partial Regex AnyBattleMissionPattern();

    // "Defeat 15 waves of enemies in any wave-based game mode, Deal 25k Piercing damage"
    [GeneratedRegex(@"^Defeat (?<count>\d+) waves of enemies in any wave-based game mode, Deal \d+k (?<label>[^,]+) damage$", RegexOptions.CultureInvariant)]
    private static partial Regex DamageTypeMissionPattern();

    // "Use abilities 15 times with Imperial units, Deal 10k damage with Abilities"
    [GeneratedRegex(@"^Use abilities (?<count>\d+) times with (?<label>[^,]+) units, Deal \d+k damage with Abilities$", RegexOptions.CultureInvariant)]
    private static partial Regex UseAbilitiesWithAllianceMissionPattern();

    // "Defeat 30 enemies with Chaos units"
    [GeneratedRegex(@"^Defeat (?<count>\d+) enemies with (?<label>[^,]+) units$", RegexOptions.CultureInvariant)]
    private static partial Regex DefeatWithAllianceMissionPattern();

    // "Defeat 75 enemies with Bolter Damage"
    [GeneratedRegex(@"^Defeat (?<count>\d+) enemies with (?<label>[^,]+) Damage$", RegexOptions.CultureInvariant)]
    private static partial Regex DefeatWithDamageTypeMissionPattern();

    // "Win 1 battle without Summoning any units"
    [GeneratedRegex(@"^Win (?<count>\d+) battles? without Summoning any units$", RegexOptions.CultureInvariant)]
    private static partial Regex WinWithoutSummonMissionPattern();

    // "Win 5 battles without deploying any Big Target characters"
    [GeneratedRegex(@"^Win (?<count>\d+) battles? without deploying any (?<label>[^,]+) characters$", RegexOptions.CultureInvariant)]
    private static partial Regex WinWithoutTraitMissionPattern();

    // "Defeat 30 Daemon enemies"
    [GeneratedRegex(@"^Defeat (?<count>\d+) (?<label>[^,]+) enemies$", RegexOptions.CultureInvariant)]
    private static partial Regex DefeatTraitEnemiesMissionPattern();

    // "Defeat 30 Necrons", "Defeat 75 AdMechs", "Defeat 100 Death Guard"
    [GeneratedRegex(@"^Defeat (?<count>\d+) (?<label>[^,\d]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DefeatFactionMissionPattern();

    // Alias tables: the label as it appears in mission text (V1's wording) -> the catalog id. Explicit on purpose:
    // a label not listed fails the load instead of guessing an id. Entries beyond today's missions follow V1's
    // labels so a refresh that reuses them parses without a code change.

    /// <summary>Faction labels (V1's faction names, with its mission-text overrides "AdMechs" and "T'au") ->
    /// units/npcs <c>factionId</c>.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LreMissionFactionAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Adepta Sororitas"] = "Sisterhood",
            ["Adeptus Astartes"] = "AdeptusAstartes",
            ["Adeptus Custodes"] = "Custodes",
            ["AdMechs"] = "AdeptusMechanicus",
            ["Aeldari"] = "Aeldari",
            ["Astra Militarum"] = "AstraMilitarum",
            ["Black Legion"] = "BlackLegion",
            ["Black Templars"] = "BlackTemplars",
            ["Blood Angels"] = "BloodAngels",
            ["Dark Angels"] = "DarkAngels",
            ["Death Guard"] = "DeathGuard",
            ["Emperor's Children"] = "EmperorsChildren",
            ["Genestealer Cults"] = "Genestealers",
            ["Leagues of Votann"] = "LeaguesOfVotann",
            ["Necrons"] = "Necrons",
            ["Orks"] = "Orks",
            ["Space Wolves"] = "SpaceWolves",
            ["T'au"] = "Tau",
            ["The Lost and the Damned"] = "TheLostAndTheDamned",
            ["Thousand Sons"] = "ThousandSons",
            ["Tyranids"] = "Tyranids",
            ["Ultramarines"] = "Ultramarines",
            ["World Eaters"] = "WorldEaters",
        };

    /// <summary>Alliance labels -> units <c>alliance</c>.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LreMissionAllianceAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Chaos"] = "Chaos",
            ["Imperial"] = "Imperial",
            ["Xenos"] = "Xenos",
        };

    /// <summary>Damage-type labels -> the damage-type strings of units/npcs (<c>meleeDamage</c>,
    /// <c>rangedDamage</c>, ability damage) and of LRE <c>DamageType</c> filters.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LreMissionDamageTypeAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Bio"] = "Bio",
            ["Blast"] = "Blast",
            ["Bolter"] = "Bolter",
            ["Chain"] = "Chain",
            ["Energy"] = "Energy",
            ["Eviscerate"] = "Eviscerate",
            ["Flame"] = "Flame",
            ["Gauss"] = "Gauss",
            ["Las"] = "Las",
            ["Melta"] = "Melta",
            ["Particle"] = "Particle",
            ["Physical"] = "Physical",
            ["Piercing"] = "Piercing",
            ["Plasma"] = "Plasma",
            ["Power"] = "Power",
            ["Projectile"] = "Projectile",
            ["Psychic"] = "Psychic",
            ["Pulse"] = "Pulse",
            ["Toxic"] = "Toxic",
        };

    /// <summary>Trait labels (V1's <c>Trait</c> enum labels) -> the catalog trait ids (units/npcs <c>traits</c>,
    /// <c>Data/enums.json</c>). Note the renamed ones: "Final Vengeance" is <c>FinalJustice</c>, "Deep Strike"
    /// is <c>TeleportStrike</c>. "Daemon" also backs the "Defeat N Daemon enemies" missions.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LreMissionTraitAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Act of Faith"] = "ActOfFaith",
            ["Ambush"] = "Ambush",
            ["Beast Slayer"] = "BeastSlayer",
            ["Big Target"] = "BigTarget",
            ["Blessings of Khorne"] = "BlessingsOfKhorne",
            ["Camouflage"] = "Camouflage",
            ["Contagions of Nurgle"] = "ContagionsOfNurgle",
            ["Crushing Strike"] = "CrushingStrike",
            ["Daemon"] = "Daemon",
            ["Deep Strike"] = "TeleportStrike",
            ["Explodes"] = "Explodes",
            ["Final Vengeance"] = "FinalJustice",
            ["Flying"] = "Flying",
            ["Get Stuck In"] = "GetStuckIn",
            ["Healer"] = "Healer",
            ["Heavy Weapon"] = "HeavyWeapon",
            ["Indirect Fire"] = "IndirectFire",
            ["Infiltrate"] = "Infiltrate",
            ["Let the Galaxy Burn"] = "LetTheGalaxyBurn",
            ["Living Metal"] = "LivingMetal",
            ["Martial Ka'tah"] = "MartialKatah",
            ["Mechanic"] = "Mechanic",
            ["Mechanical"] = "Mechanical",
            ["MK X Gravis"] = "MkXGravis",
            ["Overwatch"] = "Overwatch",
            ["Parry"] = "Parry",
            ["Psyker"] = "Psyker",
            ["Putrid Explosion"] = "PutridExplosion",
            ["Rapid Assault"] = "RapidAssault",
            ["Ranged Specialist"] = "RangedSpecialist",
            ["Resilient"] = "Resilient",
            ["Shadow in the Warp"] = "ShadowInTheWarp",
            ["Suppressive Fire"] = "SuppressiveFire",
            ["Synapse"] = "Synapse",
            ["Terminator Armour"] = "TerminatorArmour",
            ["Terrifying"] = "Terrifying",
            ["Thrill Seekers"] = "ThrillSeekers",
            ["Unstoppable"] = "Unstoppable",
            ["Vehicle"] = "Vehicle",
            ["Weaver of Fates"] = "WeaverOfFate",
        };

    /// <summary>Parses one raw mission line; null with <paramref name="problem"/> set when no template matches or
    /// the matched template's label has no alias.</summary>
    internal static GameCatalogLreMission? ParseLreMission(int index, string? text, out string? problem)
    {
        var line = text ?? string.Empty;
        foreach (var template in LreMissionTemplates)
        {
            var match = template.Pattern.Match(line);
            if (!match.Success)
                continue;

            if (!int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                problem = $"count '{match.Groups["count"].Value}' is not a number";
                return null;
            }

            string? id = null;
            if (template.Label != LreMissionLabel.None)
            {
                var label = match.Groups["label"].Value;
                var aliases = template.Label switch
                {
                    LreMissionLabel.Faction => LreMissionFactionAliases,
                    LreMissionLabel.Alliance => LreMissionAllianceAliases,
                    LreMissionLabel.DamageType => LreMissionDamageTypeAliases,
                    _ => LreMissionTraitAliases,
                };
                if (!aliases.TryGetValue(label, out id))
                {
                    problem = $"{template.Kind} names unknown {LabelName(template.Label)} '{label}' (add it to the {LabelName(template.Label)} alias table)";
                    return null;
                }
            }

            problem = null;
            return new GameCatalogLreMission(
                index,
                template.Kind,
                count,
                template.Label == LreMissionLabel.Faction ? id : null,
                template.Label == LreMissionLabel.Alliance ? id : null,
                template.Label == LreMissionLabel.DamageType ? id : null,
                template.Label == LreMissionLabel.Trait ? id : null,
                line);
        }

        problem = "matches no mission template (add one to LreMissionTemplates)";
        return null;
    }

    private static string LabelName(LreMissionLabel label) => label switch
    {
        LreMissionLabel.Faction => "faction",
        LreMissionLabel.Alliance => "alliance",
        LreMissionLabel.DamageType => "damage type",
        _ => "trait",
    };
}
