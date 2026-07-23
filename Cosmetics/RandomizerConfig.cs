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

internal readonly record struct CosmeticVariantKey(ushort DefIndex, int PaintKit);

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

    internal void Validate(CosmeticCatalog catalog)
    {
        KnifeTypes.Validate("knife type", catalog.KnifeDefinitions.ToHashSet());
        WeaponPaints.Validate(
            "weapon paint",
            catalog.Weapons
                .SelectMany(weapon => weapon.Paints.Select(
                    paint => new CosmeticVariantKey(weapon.DefIndex, paint.PaintKit)))
                .ToHashSet());
        KnifePaints.Validate(
            "knife paint",
            catalog.KnifeDefinitions
                .SelectMany(definition =>
                {
                    catalog.TryGetKnifePaints(definition, out var paints);
                    return paints.Select(
                        paint => new CosmeticVariantKey(definition, paint.PaintKit));
                })
                .ToHashSet());
        Gloves.Validate(
            "glove",
            catalog.Gloves
                .Select(glove => new CosmeticVariantKey(glove.DefIndex, glove.PaintKit))
                .ToHashSet());
        Stickers.Validate("sticker", catalog.StickerKits.Select(sticker => sticker.DefIndex).ToHashSet());
        Charms.Validate("charm", catalog.KeychainDefinitions.ToHashSet());
        Agents.Validate(
            "agent",
            RandomizerAssets.CounterTerroristModels
                .Concat(RandomizerAssets.TerroristModels)
                .ToHashSet(StringComparer.Ordinal));
        MusicKits.Validate("music kit", catalog.MusicKits.ToHashSet());
    }
}

internal sealed class RandomizerWeights
{
    public Dictionary<string, int> WeaponRarities { get; init; } = [];
    public Dictionary<string, int> KnifeTypes { get; init; } = [];
    public Dictionary<string, int> GloveFamilies { get; init; } = [];
    public Dictionary<string, int> StickerCounts { get; init; } = [];
    public Dictionary<string, int> StickerRepeatChances { get; init; } = [];
    public Dictionary<string, int> StickerFinishes { get; init; } = [];
    public Dictionary<string, int> FourRepeatFinishes { get; init; } = [];
    public Dictionary<string, int> FourMixedFinishes { get; init; } = [];
    public int CharmChance { get; init; }

    internal int GetWeaponRarityWeight(CosmeticRarity rarity)
        => Require(WeaponRarities, rarity switch
        {
            CosmeticRarity.Consumer => "consumer",
            CosmeticRarity.Industrial => "industrial",
            CosmeticRarity.MilSpec => "milSpec",
            CosmeticRarity.Restricted => "restricted",
            CosmeticRarity.Classified => "classified",
            CosmeticRarity.Covert => "covert",
            CosmeticRarity.Contraband => "contraband",
            _ => throw new ArgumentOutOfRangeException(nameof(rarity))
        });

    internal int GetKnifeTypeWeight(ushort defIndex)
        => Require(KnifeTypes, defIndex.ToString());

    internal int GetGloveFamilyWeight(ushort defIndex)
        => Require(GloveFamilies, defIndex.ToString());

    internal int GetStickerCountWeight(int count)
        => Require(StickerCounts, count.ToString());

    internal int GetStickerRepeatChance(int count)
        => StickerRepeatChances.GetValueOrDefault(count.ToString(), 100);

    internal int GetStickerFinishWeight(
        StickerFinish finish,
        bool fourSticker,
        bool repeated)
    {
        var table = fourSticker
            ? repeated ? FourRepeatFinishes : FourMixedFinishes
            : StickerFinishes;
        return Require(table, finish.ToString().ToLowerInvariant());
    }

    internal void Validate(CosmeticCatalog catalog)
    {
        ValidateProbability(
            WeaponRarities,
            ["consumer", "industrial", "milSpec", "restricted", "classified", "covert", "contraband"],
            "weapon rarity");
        ValidateProbability(
            KnifeTypes,
            catalog.KnifeDefinitions.Select(value => value.ToString()),
            "knife type");
        ValidateProbability(
            GloveFamilies,
            catalog.Gloves.Select(glove => glove.DefIndex.ToString()).Distinct(),
            "glove family");
        ValidateProbability(StickerCounts, Enumerable.Range(0, 6).Select(value => value.ToString()), "sticker count");
        var finishes = Enum.GetNames<StickerFinish>().Select(value => value.ToLowerInvariant());
        ValidateProbability(StickerFinishes, finishes, "sticker finish");
        ValidateProbability(FourRepeatFinishes, finishes, "four-repeat finish");
        ValidateProbability(FourMixedFinishes, finishes, "four-mixed finish");

        if (StickerRepeatChances.Count != 4
            || StickerRepeatChances.Any(pair =>
                pair.Key is not ("2" or "3" or "4" or "5")
                || pair.Value is < 0 or > 100)
            || CharmChance is < 0 or > 100)
        {
            throw new InvalidDataException("Randomizer config contains an invalid chance.");
        }
    }

    private static void ValidateProbability(
        IReadOnlyDictionary<string, int> weights,
        IEnumerable<string> expectedKeys,
        string label)
    {
        var expected = expectedKeys.ToHashSet(StringComparer.Ordinal);
        if (!weights.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected)
            || weights.Values.Any(value => value < 0)
            || weights.Values.Sum() != 100)
        {
            throw new InvalidDataException(
                $"Randomizer config {label} weights must contain the exact current keys and total 100.");
        }
    }

    private static int Require(IReadOnlyDictionary<string, int> weights, string key)
        => weights.TryGetValue(key, out var weight)
            ? weight
            : throw new InvalidDataException($"Randomizer config is missing weight {key}.");
}

internal sealed class RandomizerConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public int SchemaVersion { get; init; } = 2;
    public RandomizerOptions Options { get; init; } = new();
    public RandomizerWeights Weights { get; init; } = new();
    public RandomizerFilters Filters { get; init; } = new();

    internal static RandomizerConfig Load(string path, CosmeticCatalog catalog)
    {
        using var stream = File.OpenRead(path);
        var config = JsonSerializer.Deserialize<RandomizerConfig>(stream, SerializerOptions)
            ?? throw new InvalidDataException("Randomizer config is empty.");
        config.Validate(catalog);
        return config;
    }

    internal static RandomizerConfig CreateDefault(CosmeticCatalog catalog)
    {
        var knifeTypes = catalog.KnifeDefinitions.ToDictionary(
            definition => definition.ToString(),
            _ => 0);
        foreach (var knife in RandomizerAssets.Knives)
        {
            if (knifeTypes.ContainsKey(knife.DefIndex.ToString()))
                knifeTypes[knife.DefIndex.ToString()] = knife.Weight;
        }

        var config = new RandomizerConfig
        {
            Weights = new RandomizerWeights
            {
                WeaponRarities = new()
                {
                    ["consumer"] = 1,
                    ["industrial"] = 3,
                    ["milSpec"] = 10,
                    ["restricted"] = 22,
                    ["classified"] = 23,
                    ["covert"] = 40,
                    ["contraband"] = 1
                },
                KnifeTypes = knifeTypes,
                GloveFamilies = new()
                {
                    ["4725"] = 3,
                    ["5027"] = 3,
                    ["5030"] = 31,
                    ["5031"] = 16,
                    ["5032"] = 10,
                    ["5033"] = 10,
                    ["5034"] = 24,
                    ["5035"] = 3
                },
                StickerCounts = new()
                {
                    ["0"] = 35,
                    ["1"] = 12,
                    ["2"] = 8,
                    ["3"] = 8,
                    ["4"] = 25,
                    ["5"] = 12
                },
                StickerRepeatChances = new()
                {
                    ["2"] = 25,
                    ["3"] = 28,
                    ["4"] = 41,
                    ["5"] = 21
                },
                StickerFinishes = new()
                {
                    ["paper"] = 43,
                    ["glitter"] = 8,
                    ["holo"] = 28,
                    ["foil"] = 9,
                    ["gold"] = 11,
                    ["lenticular"] = 1
                },
                FourRepeatFinishes = new()
                {
                    ["paper"] = 30,
                    ["glitter"] = 11,
                    ["holo"] = 40,
                    ["foil"] = 7,
                    ["gold"] = 11,
                    ["lenticular"] = 1
                },
                FourMixedFinishes = new()
                {
                    ["paper"] = 30,
                    ["glitter"] = 5,
                    ["holo"] = 22,
                    ["foil"] = 8,
                    ["gold"] = 34,
                    ["lenticular"] = 1
                },
                CharmChance = 70
            }
        };
        config.Validate(catalog);
        return config;
    }

    internal void Validate(CosmeticCatalog catalog)
    {
        if (SchemaVersion != 2)
            throw new InvalidDataException($"Unsupported randomizer config schema {SchemaVersion}.");
        Weights.Validate(catalog);
        Filters.Validate(catalog);
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
