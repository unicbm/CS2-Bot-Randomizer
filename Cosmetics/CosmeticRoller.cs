namespace BotRandomizer;

internal sealed class CosmeticRoller
{
    private const int StickerSlabDefinition = 37;
    private const int MinimumKeychainSeed = 1;
    private const int MaximumKeychainSeed = 100000;

    private readonly Random _random;
    private readonly CosmeticCatalog _catalog;
    private readonly CharmPlacementCatalog _charmPlacements;
    private readonly WeaponWearAllocator _wearAllocator = new();
    private readonly RandomizerWeights _weights;
    private readonly IReadOnlyList<KnifeDefinition> _knives;
    private readonly IReadOnlyDictionary<ushort, IReadOnlyList<KnifePaintCatalogEntry>> _knifePaints;
    private readonly IReadOnlyDictionary<ushort, IReadOnlyList<PaintCatalogEntry>> _weaponPaints;
    private readonly IReadOnlyList<IReadOnlyList<GloveCatalogEntry>> _gloveFamilies;
    private readonly IReadOnlyList<IReadOnlyList<StickerCatalogEntry>> _stickerCategoryPools;
    private readonly IReadOnlyList<uint> _keychainDefinitions;
    private readonly IReadOnlyList<int> _musicKits;
    private readonly IReadOnlyList<string> _counterTerroristModels;
    private readonly IReadOnlyList<string> _terroristModels;

    internal CosmeticRoller(
        CosmeticCatalog catalog,
        CharmPlacementCatalog charmPlacements,
        Random? random = null)
        : this(
            catalog,
            charmPlacements,
            RandomizerConfig.CreateDefault(catalog),
            random)
    {
    }

    internal CosmeticRoller(
        CosmeticCatalog catalog,
        CharmPlacementCatalog charmPlacements,
        RandomizerConfig config,
        Random? random = null)
    {
        _catalog = catalog;
        _charmPlacements = charmPlacements;
        _weights = config.Weights;
        _random = random ?? new Random();

        var knifePaints = new Dictionary<ushort, IReadOnlyList<KnifePaintCatalogEntry>>();
        var knives = new List<KnifeDefinition>();
        foreach (var definition in catalog.KnifeDefinitions)
        {
            var weight = _weights.GetKnifeTypeWeight(definition);
            if (weight <= 0 || !config.Filters.KnifeTypes.Includes(definition))
                continue;
            if (!catalog.TryGetKnifePaints(definition, out var availablePaints))
                continue;
            var paints = availablePaints
                .Where(paint => config.Filters.KnifePaints.Includes(
                    new CosmeticVariantKey(definition, paint.PaintKit)))
                .ToArray();
            if (paints.Length == 0)
                continue;
            knives.Add(new KnifeDefinition(definition, weight));
            knifePaints.Add(definition, paints);
        }
        _knives = RequirePool(knives, "knife type/paint");
        _knifePaints = knifePaints;

        _weaponPaints = catalog.Weapons.ToDictionary(
            weapon => weapon.DefIndex,
            weapon => (IReadOnlyList<PaintCatalogEntry>)weapon.Paints
                .Where(paint => config.Filters.WeaponPaints.Includes(
                    new CosmeticVariantKey(weapon.DefIndex, paint.PaintKit)))
                .ToArray());

        _gloveFamilies = RequirePool(
            catalog.Gloves
                .Where(glove => config.Filters.Gloves.Includes(
                    new CosmeticVariantKey(glove.DefIndex, glove.PaintKit)))
                .GroupBy(glove => glove.DefIndex)
                .Select(group => (IReadOnlyList<GloveCatalogEntry>)group.ToArray())
                .Where(group => _weights.GetGloveFamilyWeight(group[0].DefIndex) > 0)
                .ToArray(),
            "glove family");

        _stickerCategoryPools = catalog.StickerCategoryPools
            .Select(pool => (IReadOnlyList<StickerCatalogEntry>)pool
                .Where(sticker => config.Filters.Stickers.Includes(sticker.DefIndex))
                .ToArray())
            .Where(pool => pool.Count > 0)
            .ToArray();
        _keychainDefinitions = catalog.KeychainDefinitions
            .Where(config.Filters.Charms.Includes)
            .ToArray();
        _musicKits = RequirePool(
            catalog.MusicKits.Where(config.Filters.MusicKits.Includes).ToArray(),
            "music kit");
        _counterTerroristModels = RequirePool(
            RandomizerAssets.CounterTerroristModels
                .Where(config.Filters.Agents.Includes)
                .ToArray(),
            "Counter-Terrorist agent");
        _terroristModels = RequirePool(
            RandomizerAssets.TerroristModels
                .Where(config.Filters.Agents.Includes)
                .ToArray(),
            "Terrorist agent");
    }

    internal int KnifeTypeCount => _knives.Count;

    internal BotCosmeticLoadout RollLoadout(byte team, int? preservedMusicKit = null)
    {
        var modelPool = team == RandomizerAssets.CounterTerroristTeam
            ? _counterTerroristModels
            : _terroristModels;
        var (knife, glove) = RollOutfit();

        return new BotCosmeticLoadout
        {
            Team = team,
            AgentModel = Pick(modelPool),
            MusicKit = preservedMusicKit ?? Pick(_musicKits),
            Knife = knife,
            Glove = glove
        };
    }

    internal WeaponCosmeticSelection? GetOrCreateWeapon(BotCosmeticLoadout loadout, ushort defIndex)
    {
        if (loadout.Weapons.TryGetValue(defIndex, out var existing))
            return existing;
        if (!_catalog.TryGetWeapon(defIndex, out var weapon)
            || !_weaponPaints.TryGetValue(defIndex, out var paints)
            || paints.Count == 0)
        {
            return null;
        }

        var paint = PickWeaponPaint(paints);
        var stickers = RollStickers(paint.Legacy
            ? weapon.LegacyStickerSchemaCount
            : weapon.StickerSchemaCount);
        var keychain = RollKeychain(defIndex);
        var wear = _wearAllocator.Reserve(defIndex, paint, stickers);
        var selection = new WeaponCosmeticSelection(
            paint.PaintKit,
            0,
            wear,
            paint.Legacy,
            stickers,
            keychain);
        loadout.Weapons.Add(defIndex, selection);
        return selection;
    }

    internal void ResetMap() => _wearAllocator.Reset();

    private (KnifeSelection Knife, GloveSelection Glove) RollOutfit()
    {
        var knifeDefinition = PickWeighted(_knives, knife => knife.Weight);
        var knifePaint = PickKnifePaint(_knifePaints[knifeDefinition.DefIndex]);
        var gloveFamily = PickWeighted(
            _gloveFamilies,
            family => _weights.GetGloveFamilyWeight(family[0].DefIndex));
        var glove = Pick(gloveFamily);
        return (
            new KnifeSelection(
                knifeDefinition.DefIndex,
                knifePaint.PaintKit,
                DefaultWear(knifePaint.WearMin, knifePaint.WearMax)),
            new GloveSelection(
                glove.DefIndex,
                glove.PaintKit,
                DefaultWear(glove.WearMin, glove.WearMax)));
    }

    private IReadOnlyList<StickerSelection> RollStickers(int schemaCount)
    {
        if (_stickerCategoryPools.Count == 0 || schemaCount <= 0)
            return Array.Empty<StickerSelection>();

        var count = Math.Min(
            PickWeighted(
                Enumerable.Range(0, 6).ToArray(),
                _weights.GetStickerCountWeight),
            schemaCount);
        if (count == 0)
            return Array.Empty<StickerSelection>();

        var category = PickStickerCategory();
        var repeated = count == 1
            || _random.Next(100) < _weights.GetStickerRepeatChance(count);
        if (!repeated && category.Count < count)
        {
            var eligibleCategories = _stickerCategoryPools
                .Where(candidate => candidate.Count >= count)
                .ToArray();
            if (eligibleCategories.Length > 0)
            {
                category = PickWeighted(
                    eligibleCategories,
                    candidate => Math.Max(1, (int)Math.Round(Math.Sqrt(candidate.Count))));
            }
            else
            {
                repeated = true;
            }
        }
        var craftPool = count == 4
            ? PickFourStickerFinishPool(category, repeated)
            : category;
        if (repeated)
            return RepeatSticker(PickSticker(craftPool), count);

        var selections = new StickerSelection[count];
        var used = new HashSet<uint>();
        for (var slot = 0; slot < count; slot++)
        {
            var candidates = craftPool
                .Where(sticker => !used.Contains(sticker.DefIndex))
                .ToArray();
            var sticker = PickSticker(candidates.Length > 0 ? candidates : craftPool);
            used.Add(sticker.DefIndex);
            selections[slot] = CreateSticker(sticker, slot);
        }
        return selections;
    }

    private IReadOnlyList<StickerSelection> RepeatSticker(
        StickerCatalogEntry sticker,
        int count)
    {
        var selections = new StickerSelection[count];
        for (var slot = 0; slot < count; slot++)
            selections[slot] = CreateSticker(sticker, slot);
        return selections;
    }

    private static StickerSelection CreateSticker(StickerCatalogEntry sticker, int slot)
        => new(sticker.DefIndex, slot, (uint)slot);

    private IReadOnlyList<StickerCatalogEntry> PickStickerCategory(int minimumCount = 1)
        => PickWeighted(
            _stickerCategoryPools
                .Where(category => category.Count >= minimumCount)
                .ToArray(),
            category => Math.Max(1, (int)Math.Round(Math.Sqrt(category.Count))));

    private StickerCatalogEntry PickSticker(IReadOnlyList<StickerCatalogEntry> stickers)
    {
        var finishPools = stickers
            .GroupBy(sticker => sticker.Finish)
            .Select(group => (IReadOnlyList<StickerCatalogEntry>)group.ToArray())
            .ToArray();
        var finishPool = PickWeighted(
            finishPools,
            pool => _weights.GetStickerFinishWeight(
                pool[0].Finish,
                fourSticker: false,
                repeated: false));
        return Pick(finishPool);
    }

    private IReadOnlyList<StickerCatalogEntry> PickFourStickerFinishPool(
        IReadOnlyList<StickerCatalogEntry> stickers,
        bool repeated)
    {
        var finishPools = stickers
            .GroupBy(sticker => sticker.Finish)
            .Select(group => (IReadOnlyList<StickerCatalogEntry>)group.ToArray())
            .ToArray();
        var eligiblePools = repeated
            ? finishPools
            : finishPools.Where(pool => pool.Count >= 4).ToArray();
        return PickWeighted(
            eligiblePools.Length > 0 ? eligiblePools : finishPools,
            pool => _weights.GetStickerFinishWeight(
                pool[0].Finish,
                fourSticker: true,
                repeated));
    }

    private KeychainSelection? RollKeychain(ushort weaponDefIndex)
    {
        if (_keychainDefinitions.Count == 0
            || _random.Next(100) >= _weights.CharmChance)
        {
            return null;
        }

        var definition = Pick(_keychainDefinitions);
        var sticker = definition == StickerSlabDefinition && _stickerCategoryPools.Count > 0
            ? PickSticker(PickStickerCategory()).DefIndex
            : (uint?)null;
        var placement = _charmPlacements.TryGetPlacements(weaponDefIndex, out var placements)
            ? Pick(placements)
            : (CharmPlacement?)null;
        return new KeychainSelection(
            definition,
            _random.Next(MinimumKeychainSeed, MaximumKeychainSeed + 1),
            Sticker: sticker,
            X: placement?.X,
            Y: placement?.Y,
            Z: placement?.Z);
    }

    private PaintCatalogEntry PickWeaponPaint(IReadOnlyList<PaintCatalogEntry> paints)
    {
        var rarityPools = paints
            .GroupBy(paint => paint.Rarity)
            .Select(group => (IReadOnlyList<PaintCatalogEntry>)group.ToArray())
            .ToArray();
        var rarityPool = PickWeighted(
            rarityPools,
            pool => _weights.GetWeaponRarityWeight(pool[0].Rarity));
        return Pick(rarityPool);
    }

    private KnifePaintCatalogEntry PickKnifePaint(
        IReadOnlyList<KnifePaintCatalogEntry> paints)
    {
        var finishPools = paints
            .GroupBy(paint => paint.Finish, StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<KnifePaintCatalogEntry>)group.ToArray())
            .ToArray();
        var finishPool = PickWeighted(
            finishPools,
            pool => _catalog.GetKnifeFinishWeight(pool[0].Finish));
        return Pick(finishPool);
    }

    private T Pick<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
            throw new InvalidOperationException("Cannot roll from an empty cosmetic pool.");
        return values[_random.Next(values.Count)];
    }

    private T PickWeighted<T>(IReadOnlyList<T> values, Func<T, int> getWeight)
    {
        var totalWeight = 0;
        foreach (var value in values)
        {
            var weight = getWeight(value);
            if (weight < 0)
                throw new InvalidOperationException("Cosmetic weights cannot be negative.");
            totalWeight = checked(totalWeight + weight);
        }

        if (totalWeight == 0)
            return Pick(values);

        var roll = _random.Next(totalWeight);
        foreach (var value in values)
        {
            var weight = getWeight(value);
            if (roll < weight)
                return value;
            roll -= weight;
        }

        throw new InvalidOperationException("Weighted cosmetic selection did not resolve.");
    }

    private static float DefaultWear(float minimum, float maximum)
        => Math.Clamp(0.01f, minimum, maximum);

    private static IReadOnlyList<T> RequirePool<T>(IReadOnlyList<T> values, string label)
        => values.Count > 0
            ? values
            : throw new InvalidDataException($"Randomizer config leaves the {label} pool empty.");
}
