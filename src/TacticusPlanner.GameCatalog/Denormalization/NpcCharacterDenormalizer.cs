using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>
    /// Links each NPC to the playable character it is a version of: the character with the same game name in the same
    /// faction (a boss, a survival or event copy of a hero), unless the curated <c>npc-characters</c> file says
    /// otherwise for that NPC (a character id for a name that differs, <c>null</c> for a same-named NPC that is not the
    /// character). Machines of War are not characters, so an NPC copy of a MoW has none. Returns only the linked NPCs.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A curated id is not an NPC, a curated character id is not a character, or a curated entry repeats what the name
    /// match already gives (the list stays the short set of exceptions).
    /// </exception>
    public static IReadOnlyDictionary<string, string> ReadNpcCharacters(
        IReadOnlyDictionary<string, string?> curated,
        IReadOnlyDictionary<string, GameCatalogFactionNpcs> npcsByFaction,
        IReadOnlyDictionary<string, GameCatalogFactionUnits> unitsByFaction)
    {
        var characterIds = unitsByFaction.Values
            .SelectMany(faction => faction.Characters)
            .Select(character => character.Id)
            .ToHashSet(StringComparer.Ordinal);
        // Character names are unique within a faction in the game; the first wins should a bump ever repeat one.
        var characterByFactionAndName = unitsByFaction.Values
            .SelectMany(faction => faction.Characters.Select(character => (faction.FactionId, character)))
            .GroupBy(entry => (entry.FactionId, entry.character.Name))
            .ToDictionary(group => group.Key, group => group.First().character.Id);

        var nameMatches = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var faction in npcsByFaction.Values)
        {
            foreach (var npc in faction.Npcs)
            {
                if (characterByFactionAndName.TryGetValue((faction.FactionId, npc.Name), out var characterId))
                {
                    nameMatches[npc.Id] = characterId;
                }
            }
        }

        var npcIds = npcsByFaction.Values
            .SelectMany(faction => faction.Npcs)
            .Select(npc => npc.Id)
            .ToHashSet(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var (npcId, characterId) in curated.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!npcIds.Contains(npcId))
            {
                errors.Add($"'{npcId}' is not an NPC");
            }

            if (characterId is not null && !characterIds.Contains(characterId))
            {
                errors.Add($"'{npcId}' links '{characterId}', which is not a character");
            }

            if (string.Equals(nameMatches.GetValueOrDefault(npcId), characterId, StringComparison.Ordinal))
            {
                errors.Add($"'{npcId}' repeats what the name match gives");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"GameCatalog dataset '{GameCatalogDatasets.NpcCharacters}': {string.Join("; ", errors)}.");
        }

        foreach (var (npcId, characterId) in curated)
        {
            if (characterId is null)
            {
                nameMatches.Remove(npcId);
            }
            else
            {
                nameMatches[npcId] = characterId;
            }
        }

        return nameMatches;
    }
}
