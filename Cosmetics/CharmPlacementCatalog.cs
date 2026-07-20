using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotRandomizer;

internal sealed class CharmPlacementCatalog
{
    private const int CurrentSchemaVersion = 1;

    private readonly Dictionary<ushort, IReadOnlyList<CharmPlacement>> _placements;

    private CharmPlacementCatalog(PlacementCatalogDocument document)
    {
        SourceDemoCount = document.Source.DemosParsed;
        ContributingDemoCount = document.Source.DemoSha256.Count;
        _placements = document.Weapons.ToDictionary(
            weapon => weapon.DefIndex,
            weapon => (IReadOnlyList<CharmPlacement>)weapon.Placements
                .Select(position => new CharmPlacement(position.X, position.Y, position.Z))
                .ToArray());
    }

    internal int SourceDemoCount { get; }
    internal int ContributingDemoCount { get; }
    internal int WeaponCount => _placements.Count;
    internal int PlacementCount => _placements.Values.Sum(placements => placements.Count);

    internal static CharmPlacementCatalog Load(string path, CosmeticCatalog cosmeticCatalog)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<PlacementCatalogDocument>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? throw new InvalidDataException("Charm placement catalog is empty.");

        Validate(document, cosmeticCatalog);
        return new CharmPlacementCatalog(document);
    }

    internal bool TryGetPlacements(ushort weaponDefIndex, out IReadOnlyList<CharmPlacement> placements)
        => _placements.TryGetValue(weaponDefIndex, out placements!);

    private static void Validate(PlacementCatalogDocument document, CosmeticCatalog cosmeticCatalog)
    {
        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported charm placement schema {document.SchemaVersion}.");

        if (document.Source.Kind != "demo-observed"
            || document.Source.Parser != "cs2-demo-botmimic/cs2-demotracer"
            || document.Source.DemosParsed <= 0
            || document.Source.DemoSha256.Count == 0
            || document.Source.DemoSha256.Count > document.Source.DemosParsed)
        {
            throw new InvalidDataException("Charm placement source metadata is invalid.");
        }

        EnsureUnique(document.Source.DemoSha256, "source demo hash");
        if (document.Source.DemoSha256.Any(hash => hash.Length != 64 || hash.Any(ch => !Uri.IsHexDigit(ch))))
            throw new InvalidDataException("Charm placement catalog contains an invalid demo hash.");

        if (document.Weapons.Count == 0)
            throw new InvalidDataException("Charm placement catalog contains no weapons.");
        EnsureUnique(document.Weapons.Select(weapon => weapon.DefIndex), "weapon definition");

        foreach (var weapon in document.Weapons)
        {
            if (!cosmeticCatalog.TryGetWeapon(weapon.DefIndex, out var catalogWeapon)
                || catalogWeapon.DesignerName != weapon.DesignerName)
            {
                throw new InvalidDataException(
                    $"Charm placement weapon {weapon.DefIndex}/{weapon.DesignerName} does not match the cosmetic catalog.");
            }

            if (weapon.Placements.Count == 0)
                throw new InvalidDataException($"Charm placement weapon {weapon.DefIndex} has no positions.");

            var uniquePlacements = new HashSet<CharmPlacement>();
            foreach (var placement in weapon.Placements)
            {
                if (!float.IsFinite(placement.X)
                    || !float.IsFinite(placement.Y)
                    || !float.IsFinite(placement.Z)
                    || placement.Observations <= 0
                    || placement.DemoCount <= 0
                    || placement.DemoCount > placement.Observations)
                {
                    throw new InvalidDataException(
                        $"Charm placement weapon {weapon.DefIndex} contains an invalid position.");
                }

                if (!uniquePlacements.Add(new CharmPlacement(placement.X, placement.Y, placement.Z)))
                    throw new InvalidDataException($"Charm placement weapon {weapon.DefIndex} contains a duplicate position.");
            }
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string label)
        where T : notnull
    {
        var set = new HashSet<T>();
        foreach (var value in values)
        {
            if (!set.Add(value))
                throw new InvalidDataException($"Charm placement catalog contains duplicate {label}: {value}.");
        }
    }

    private sealed class PlacementCatalogDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("source")]
        public PlacementSourceDocument Source { get; init; } = new();

        [JsonPropertyName("weapons")]
        public List<WeaponPlacementDocument> Weapons { get; init; } = [];
    }

    private sealed class PlacementSourceDocument
    {
        [JsonPropertyName("kind")]
        public string Kind { get; init; } = string.Empty;

        [JsonPropertyName("parser")]
        public string Parser { get; init; } = string.Empty;

        [JsonPropertyName("demosParsed")]
        public int DemosParsed { get; init; }

        [JsonPropertyName("demoSha256")]
        public List<string> DemoSha256 { get; init; } = [];
    }

    private sealed class WeaponPlacementDocument
    {
        [JsonPropertyName("defIndex")]
        public ushort DefIndex { get; init; }

        [JsonPropertyName("designerName")]
        public string DesignerName { get; init; } = string.Empty;

        [JsonPropertyName("placements")]
        public List<PlacementDocument> Placements { get; init; } = [];
    }

    private sealed class PlacementDocument
    {
        [JsonPropertyName("x")]
        public float X { get; init; }

        [JsonPropertyName("y")]
        public float Y { get; init; }

        [JsonPropertyName("z")]
        public float Z { get; init; }

        [JsonPropertyName("observations")]
        public int Observations { get; init; }

        [JsonPropertyName("demoCount")]
        public int DemoCount { get; init; }
    }
}
