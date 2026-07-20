using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotRandomizer;

internal sealed class RandomizerAssetCatalog
{
    private RandomizerAssetCatalog(AssetDocument document)
    {
        Knives = document.Knives;
        CounterTerroristAgents = document.Agents
            .Where(agent => agent.Team == RandomizerAssets.CounterTerroristTeam)
            .ToArray();
        TerroristAgents = document.Agents
            .Where(agent => agent.Team == RandomizerAssets.TerroristTeam)
            .ToArray();
        KnifeDefIndexByName = Knives.ToDictionary(
            knife => knife.DesignerName,
            knife => knife.DefIndex,
            StringComparer.Ordinal);
    }

    internal IReadOnlyList<KnifeDefinition> Knives { get; }
    internal IReadOnlyList<AgentDefinition> CounterTerroristAgents { get; }
    internal IReadOnlyList<AgentDefinition> TerroristAgents { get; }
    internal IReadOnlyDictionary<string, ushort> KnifeDefIndexByName { get; }

    internal static RandomizerAssetCatalog Load(string path, CosmeticCatalog cosmeticCatalog)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<AssetDocument>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? throw new InvalidDataException("Randomizer asset catalog is empty.");

        Validate(document, cosmeticCatalog);
        return new RandomizerAssetCatalog(document);
    }

    private static void Validate(AssetDocument document, CosmeticCatalog cosmeticCatalog)
    {
        if (document.SchemaVersion != 2)
            throw new InvalidDataException($"Unsupported randomizer asset schema {document.SchemaVersion}.");
        if (document.Knives.Count == 0 || document.Agents.Count == 0)
            throw new InvalidDataException("Cosmetic catalog is missing configurable knife or agent assets.");

        EnsureUnique(document.Knives.Select(knife => knife.DefIndex), "knife definition");
        EnsureUnique(document.Knives.Select(knife => knife.DesignerName), "knife designer name");
        EnsureUnique(document.Agents.Select(agent => agent.ModelPath), "agent model");

        foreach (var knife in document.Knives)
        {
            if (knife.DefIndex == 0
                || string.IsNullOrWhiteSpace(knife.Name)
                || string.IsNullOrWhiteSpace(knife.NameZh)
                || !knife.DesignerName.StartsWith("weapon_", StringComparison.Ordinal)
                || !cosmeticCatalog.TryGetKnifePaints(knife.DefIndex, out _))
            {
                throw new InvalidDataException($"Knife asset {knife.DefIndex} is invalid or absent from the cosmetic catalog.");
            }
        }

        foreach (var agent in document.Agents)
        {
            if (agent.Team is not (RandomizerAssets.TerroristTeam or RandomizerAssets.CounterTerroristTeam)
                || !agent.ModelPath.StartsWith("agents\\models\\", StringComparison.Ordinal)
                || !agent.ModelPath.EndsWith(".vmdl", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Agent asset '{agent.ModelPath}' is invalid.");
            }
        }

        if (!document.Agents.Any(agent => agent.Team == RandomizerAssets.TerroristTeam)
            || !document.Agents.Any(agent => agent.Team == RandomizerAssets.CounterTerroristTeam))
        {
            throw new InvalidDataException("Randomizer asset catalog must contain both teams.");
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string label)
        where T : notnull
    {
        var seen = new HashSet<T>();
        foreach (var value in values)
        {
            if (!seen.Add(value))
                throw new InvalidDataException($"Randomizer asset catalog contains duplicate {label}: {value}.");
        }
    }

    private sealed class AssetDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("knives")]
        public List<KnifeDefinition> Knives { get; init; } = [];

        [JsonPropertyName("agents")]
        public List<AgentDefinition> Agents { get; init; } = [];
    }
}
