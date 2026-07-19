using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotRandomizer;

internal sealed class CosmeticCatalog
{
    private readonly Dictionary<ushort, WeaponCatalogEntry> _weapons;
    private readonly Dictionary<string, WeaponCatalogEntry> _weaponsByDesignerName;
    private readonly Dictionary<ushort, IReadOnlyList<PaintCatalogEntry>> _knives;

    private CosmeticCatalog(CatalogDocument document)
    {
        SourceRepository = document.Source.Repository;
        SourceCommit = document.Source.Commit;
        _weapons = document.Weapons.ToDictionary(entry => entry.DefIndex);
        _weaponsByDesignerName = document.Weapons.ToDictionary(
            entry => entry.DesignerName,
            StringComparer.Ordinal);
        _knives = document.Knives.ToDictionary(
            entry => entry.DefIndex,
            entry => (IReadOnlyList<PaintCatalogEntry>)entry.Paints);
        Gloves = document.Gloves;
        StickerKits = document.StickerKits;
        KeychainDefinitions = document.KeychainDefinitions;
        MusicKits = document.MusicKits;
    }

    internal string SourceRepository { get; }
    internal string SourceCommit { get; }
    internal IReadOnlyList<GloveCatalogEntry> Gloves { get; }
    internal IReadOnlyList<uint> StickerKits { get; }
    internal IReadOnlyList<uint> KeychainDefinitions { get; }
    internal IReadOnlyList<int> MusicKits { get; }
    internal IReadOnlyCollection<WeaponCatalogEntry> Weapons => _weapons.Values;
    internal int WeaponCount => _weapons.Count;
    internal int WeaponPaintCount => _weapons.Values.Sum(entry => entry.Paints.Count);
    internal int KnifePaintCount => _knives.Values.Sum(entry => entry.Count);

    internal static CosmeticCatalog Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<CatalogDocument>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        })
            ?? throw new InvalidDataException("Cosmetic catalog is empty.");
        Validate(document);
        return new CosmeticCatalog(document);
    }

    internal bool TryGetWeapon(ushort defIndex, out WeaponCatalogEntry entry)
        => _weapons.TryGetValue(defIndex, out entry!);

    internal bool TryGetWeapon(string designerName, out WeaponCatalogEntry entry)
        => _weaponsByDesignerName.TryGetValue(designerName, out entry!);

    internal bool TryGetKnifePaints(ushort defIndex, out IReadOnlyList<PaintCatalogEntry> paints)
        => _knives.TryGetValue(defIndex, out paints!);

    private static void Validate(CatalogDocument document)
    {
        if (document.Source is null
            || document.Source.Repository != "ianlucas/cs2-lib"
            || document.Source.Commit.Length != 40
            || document.Source.Commit.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new InvalidDataException("Catalog source metadata is invalid.");
        }

        if (document.Weapons.Count == 0
            || document.Knives.Count == 0
            || document.Gloves.Count == 0
            || document.StickerKits.Count == 0
            || document.KeychainDefinitions.Count == 0
            || document.MusicKits.Count == 0)
        {
            throw new InvalidDataException("Catalog is missing a required cosmetic family.");
        }

        EnsureUnique(document.Weapons.Select(entry => entry.DefIndex), "weapon definition");
        EnsureUnique(document.Weapons.Select(entry => entry.DesignerName), "weapon designer name");
        EnsureUnique(document.Knives.Select(entry => entry.DefIndex), "knife definition");
        EnsureUnique(document.Gloves.Select(entry => (entry.DefIndex, entry.PaintKit)), "glove variant");
        EnsureUnique(document.StickerKits, "sticker kit");
        EnsureUnique(document.KeychainDefinitions, "keychain definition");
        EnsureUnique(document.MusicKits, "music kit");

        foreach (var weapon in document.Weapons)
        {
            if (weapon.DefIndex == 0
                || !weapon.DesignerName.StartsWith("weapon_", StringComparison.Ordinal)
                || weapon.Paints.Count == 0)
            {
                throw new InvalidDataException($"Weapon {weapon.DefIndex} has no valid paints.");
            }
            if (weapon.StickerSchemaCount <= 0 || weapon.LegacyStickerSchemaCount <= 0)
                throw new InvalidDataException($"Weapon {weapon.DefIndex} has invalid sticker schemas.");
            ValidatePaints(weapon.Paints, $"weapon {weapon.DefIndex}");
        }

        foreach (var knife in document.Knives)
        {
            if (knife.DefIndex == 0 || knife.Paints.Count == 0)
                throw new InvalidDataException($"Knife {knife.DefIndex} has no valid paints.");
            ValidatePaints(knife.Paints, $"knife {knife.DefIndex}");
        }

        foreach (var glove in document.Gloves)
        {
            if (glove.DefIndex == 0 || glove.PaintKit <= 0)
                throw new InvalidDataException("Catalog contains an invalid glove variant.");
            ValidateWear(glove.WearMin, glove.WearMax, $"glove {glove.DefIndex}/{glove.PaintKit}");
        }

        if (document.StickerKits.Any(value => value == 0)
            || document.KeychainDefinitions.Any(value => value == 0)
            || !document.KeychainDefinitions.Contains(37)
            || document.MusicKits.Any(value => value <= 0))
        {
            throw new InvalidDataException("Catalog contains invalid cosmetic indexes.");
        }
    }

    private static void ValidatePaints(IReadOnlyList<PaintCatalogEntry> paints, string owner)
    {
        EnsureUnique(paints.Select(entry => entry.PaintKit), $"{owner} paint");
        foreach (var paint in paints)
        {
            if (paint.PaintKit <= 0)
                throw new InvalidDataException($"{owner} contains an invalid paint kit.");
            ValidateWear(paint.WearMin, paint.WearMax, $"{owner}/{paint.PaintKit}");
        }
    }

    private static void ValidateWear(float minimum, float maximum, string owner)
    {
        if (!float.IsFinite(minimum)
            || !float.IsFinite(maximum)
            || minimum < 0.0f
            || maximum > 1.0f
            || minimum > maximum)
        {
            throw new InvalidDataException($"{owner} has an invalid wear range.");
        }

        var firstRepresentableTick = (int)Math.Ceiling(minimum * 1000.0f);
        var lastRepresentableTick = (int)Math.Floor(maximum * 1000.0f);
        if (firstRepresentableTick > lastRepresentableTick)
            throw new InvalidDataException($"{owner} has no representable wear tick.");
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string label)
        where T : notnull
    {
        var set = new HashSet<T>();
        foreach (var value in values)
        {
            if (!set.Add(value))
                throw new InvalidDataException($"Catalog contains duplicate {label}: {value}.");
        }
    }

    private sealed class CatalogDocument
    {
        [JsonPropertyName("source")]
        public CatalogSource Source { get; init; } = new();

        [JsonPropertyName("weapons")]
        public List<WeaponCatalogEntry> Weapons { get; init; } = [];

        [JsonPropertyName("knives")]
        public List<KnifeCatalogDocument> Knives { get; init; } = [];

        [JsonPropertyName("gloves")]
        public List<GloveCatalogEntry> Gloves { get; init; } = [];

        [JsonPropertyName("stickerKits")]
        public List<uint> StickerKits { get; init; } = [];

        [JsonPropertyName("keychainDefinitions")]
        public List<uint> KeychainDefinitions { get; init; } = [];

        [JsonPropertyName("musicKits")]
        public List<int> MusicKits { get; init; } = [];
    }

    private sealed class CatalogSource
    {
        [JsonPropertyName("repository")]
        public string Repository { get; init; } = string.Empty;

        [JsonPropertyName("commit")]
        public string Commit { get; init; } = string.Empty;
    }

    private sealed class KnifeCatalogDocument
    {
        [JsonPropertyName("defIndex")]
        public ushort DefIndex { get; init; }

        [JsonPropertyName("paints")]
        public List<PaintCatalogEntry> Paints { get; init; } = [];
    }
}
