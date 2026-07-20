using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotRandomizer;

internal enum FilterMode
{
    Allow,
    Deny
}

internal sealed class SelectionFilter<T>
    where T : notnull
{
    public FilterMode Mode { get; init; } = FilterMode.Deny;
    public List<T> Items { get; init; } = [];

    internal bool Includes(T value)
        => Mode == FilterMode.Allow ? Items.Contains(value) : !Items.Contains(value);

    internal void Validate(string label, IReadOnlySet<T> available)
    {
        if (Items.Count != Items.Distinct().Count())
            throw new InvalidDataException($"Randomizer config contains duplicate {label} entries.");

        foreach (var item in Items)
        {
            if (!available.Contains(item))
                throw new InvalidDataException($"Randomizer config contains unknown {label}: {item}.");
        }
    }
}

internal sealed class RandomizerFilters
{
    public SelectionFilter<ushort> KnifeTypes { get; init; } = new();

    public SelectionFilter<CosmeticVariantKey> WeaponPaints { get; init; } = new();
    public SelectionFilter<CosmeticVariantKey> KnifePaints { get; init; } = new();
    public SelectionFilter<CosmeticVariantKey> Gloves { get; init; } = new();
    public SelectionFilter<uint> Stickers { get; init; } = new();
    public SelectionFilter<uint> Charms { get; init; } = new();
    public SelectionFilter<string> Agents { get; init; } = new();
    public SelectionFilter<int> MusicKits { get; init; } = new();
}

internal sealed class RandomizerConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public int SchemaVersion { get; init; } = 1;
    public RandomizerOptions Options { get; init; } = new();
    public RandomizerFilters Filters { get; init; } = new();

    internal static RandomizerConfig CreateDefault() => new();

    internal static RandomizerConfig Load(
        string path,
        CosmeticCatalog cosmeticCatalog,
        RandomizerAssetCatalog assetCatalog)
    {
        using var stream = File.OpenRead(path);
        var config = JsonSerializer.Deserialize<RandomizerConfig>(stream, SerializerOptions)
            ?? throw new InvalidDataException("Randomizer config is empty.");
        config.Validate(cosmeticCatalog, assetCatalog);
        return config;
    }

    internal void Validate(
        CosmeticCatalog cosmeticCatalog,
        RandomizerAssetCatalog assetCatalog)
    {
        if (SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported randomizer config schema {SchemaVersion}.");

        Filters.KnifeTypes.Validate(
            "knife type",
            assetCatalog.Knives.Select(knife => knife.DefIndex).ToHashSet());
        Filters.WeaponPaints.Validate("weapon paint", cosmeticCatalog.WeaponVariants);
        Filters.KnifePaints.Validate("knife paint", cosmeticCatalog.KnifeVariants);
        Filters.Gloves.Validate("glove", cosmeticCatalog.GloveVariants);
        Filters.Stickers.Validate("sticker", cosmeticCatalog.StickerKits.ToHashSet());
        Filters.Charms.Validate("charm", cosmeticCatalog.KeychainDefinitions.ToHashSet());
        Filters.Agents.Validate(
            "agent",
            assetCatalog.CounterTerroristAgents
                .Concat(assetCatalog.TerroristAgents)
                .Select(agent => agent.ModelPath)
                .ToHashSet(StringComparer.Ordinal));
        Filters.MusicKits.Validate("music kit", cosmeticCatalog.MusicKits.ToHashSet());
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }
}
