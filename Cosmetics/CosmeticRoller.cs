namespace BotRandomizer;

internal sealed class CosmeticRoller
{
    private const int MaximumStickers = 5;
    private const int StickerSlabDefinition = 37;
    private const int MinimumKeychainSeed = 1;
    private const int MaximumKeychainSeed = 100000;
    private const int KeychainChanceNumerator = 7;
    private const int KeychainChanceDenominator = 10;

    private readonly Random _random;
    private readonly CharmPlacementCatalog _charmPlacements;
    private readonly WeaponWearAllocator _wearAllocator = new();
    private readonly IReadOnlyList<KnifeDefinition> _knives;
    private readonly IReadOnlyDictionary<ushort, IReadOnlyList<PaintCatalogEntry>> _knifePaints;
    private readonly IReadOnlyDictionary<ushort, IReadOnlyList<PaintCatalogEntry>> _weaponPaints;
    private readonly IReadOnlyDictionary<ushort, WeaponCatalogEntry> _weaponCatalog;
    private readonly IReadOnlyList<GloveCatalogEntry> _gloves;
    private readonly IReadOnlyList<uint> _stickerKits;
    private readonly IReadOnlyList<uint> _keychainDefinitions;
    private readonly IReadOnlyList<int> _musicKits;
    private readonly IReadOnlyList<string> _counterTerroristModels;
    private readonly IReadOnlyList<string> _terroristModels;
    private readonly WeaponRandomizationMode _weaponMode;

    internal CosmeticRoller(
        CosmeticCatalog catalog,
        CharmPlacementCatalog charmPlacements,
        RandomizerAssetCatalog assets,
        RandomizerConfig config,
        Random? random = null)
    {
        _charmPlacements = charmPlacements;
        _random = random ?? new Random();
        _weaponMode = config.Options.WeaponMode;

        var knifePaints = new Dictionary<ushort, IReadOnlyList<PaintCatalogEntry>>();
        var knives = new List<KnifeDefinition>();
        foreach (var knife in assets.Knives.Where(
            knife => config.Filters.KnifeTypes.Includes(knife.DefIndex)))
        {
            if (!catalog.TryGetKnifePaints(knife.DefIndex, out var availablePaints))
                continue;
            var paints = availablePaints
                .Where(paint => config.Filters.KnifePaints.Includes(
                    new CosmeticVariantKey(knife.DefIndex, paint.PaintKit)))
                .ToArray();
            if (paints.Length == 0)
                continue;
            knives.Add(knife);
            knifePaints.Add(knife.DefIndex, paints);
        }
        _knives = RequirePool(knives, "knife type/paint");
        _knifePaints = knifePaints;

        _weaponCatalog = catalog.Weapons.ToDictionary(weapon => weapon.DefIndex);
        _weaponPaints = catalog.Weapons.ToDictionary(
            weapon => weapon.DefIndex,
            weapon => (IReadOnlyList<PaintCatalogEntry>)weapon.Paints
                .Where(paint => config.Filters.WeaponPaints.Includes(
                    new CosmeticVariantKey(weapon.DefIndex, paint.PaintKit)))
                .ToArray());
        _gloves = RequirePool(
            catalog.Gloves.Where(glove => config.Filters.Gloves.Includes(
                new CosmeticVariantKey(glove.DefIndex, glove.PaintKit))).ToArray(),
            "glove");
        _stickerKits = catalog.StickerKits
            .Where(config.Filters.Stickers.Includes)
            .ToArray();
        _keychainDefinitions = catalog.KeychainDefinitions
            .Where(config.Filters.Charms.Includes)
            .ToArray();
        _musicKits = RequirePool(
            catalog.MusicKits.Where(config.Filters.MusicKits.Includes).ToArray(),
            "music kit");
        _counterTerroristModels = RequirePool(
            assets.CounterTerroristAgents
                .Select(agent => agent.ModelPath)
                .Where(config.Filters.Agents.Includes)
                .ToArray(),
            "Counter-Terrorist agent");
        _terroristModels = RequirePool(
            assets.TerroristAgents
                .Select(agent => agent.ModelPath)
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
        var knifeDefinition = Pick(_knives);
        var knifePaints = _knifePaints[knifeDefinition.DefIndex];

        var knifePaint = Pick(knifePaints);
        var glove = Pick(_gloves);

        return new BotCosmeticLoadout
        {
            Team = team,
            AgentModel = Pick(modelPool),
            MusicKit = preservedMusicKit ?? Pick(_musicKits),
            Knife = new KnifeSelection(
                knifeDefinition.DefIndex,
                knifePaint.PaintKit,
                DefaultWear(knifePaint.WearMin, knifePaint.WearMax)),
            Glove = new GloveSelection(
                glove.DefIndex,
                glove.PaintKit,
                DefaultWear(glove.WearMin, glove.WearMax))
        };
    }

    internal WeaponCosmeticSelection? GetOrCreateWeapon(BotCosmeticLoadout loadout, ushort defIndex)
    {
        if (_weaponMode == WeaponRandomizationMode.Persistent
            && loadout.Weapons.TryGetValue(defIndex, out var existing))
            return existing;
        if (!_weaponCatalog.TryGetValue(defIndex, out var weapon)
            || !_weaponPaints.TryGetValue(defIndex, out var paints)
            || paints.Count == 0)
            return null;

        var paint = Pick(paints);
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
        if (_weaponMode == WeaponRandomizationMode.Persistent)
            loadout.Weapons[defIndex] = selection;
        return selection;
    }

    internal void ResetMap() => _wearAllocator.Reset();

    private IReadOnlyList<StickerSelection> RollStickers(int schemaCount)
    {
        if (_stickerKits.Count == 0 || schemaCount <= 0)
            return Array.Empty<StickerSelection>();

        var count = _random.Next(MaximumStickers + 1);
        var stickers = new StickerSelection[count];
        for (var slot = 0; slot < count; slot++)
        {
            stickers[slot] = new StickerSelection(
                Pick(_stickerKits),
                slot,
                (uint)(slot % schemaCount));
        }
        return stickers;
    }

    private KeychainSelection? RollKeychain(ushort weaponDefIndex)
    {
        if (_keychainDefinitions.Count == 0
            || _random.Next(KeychainChanceDenominator) >= KeychainChanceNumerator)
            return null;

        var definition = Pick(_keychainDefinitions);
        var sticker = definition == StickerSlabDefinition && _stickerKits.Count > 0
            ? Pick(_stickerKits)
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

    private T Pick<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
            throw new InvalidOperationException("Cannot roll from an empty cosmetic pool.");
        return values[_random.Next(values.Count)];
    }

    private static float DefaultWear(float minimum, float maximum)
        => Math.Clamp(0.01f, minimum, maximum);

    private static IReadOnlyList<T> RequirePool<T>(IReadOnlyList<T> values, string label)
    {
        if (values.Count == 0)
            throw new InvalidDataException($"Randomizer config leaves the {label} pool empty.");
        return values;
    }
}
