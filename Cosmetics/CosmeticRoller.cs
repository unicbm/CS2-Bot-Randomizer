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
    private readonly CosmeticCatalog _catalog;
    private readonly CharmPlacementCatalog _charmPlacements;
    private readonly WeaponWearAllocator _wearAllocator = new();

    internal CosmeticRoller(
        CosmeticCatalog catalog,
        CharmPlacementCatalog charmPlacements,
        Random? random = null)
    {
        _catalog = catalog;
        _charmPlacements = charmPlacements;
        _random = random ?? new Random();
    }

    internal BotCosmeticLoadout RollLoadout(byte team, int? preservedMusicKit = null)
    {
        var modelPool = team == RandomizerAssets.CounterTerroristTeam
            ? RandomizerAssets.CounterTerroristModels
            : RandomizerAssets.TerroristModels;
        var knifeDefinition = Pick(RandomizerAssets.Knives);
        if (!_catalog.TryGetKnifePaints(knifeDefinition.DefIndex, out var knifePaints))
            throw new InvalidOperationException($"No paint catalog for knife {knifeDefinition.DefIndex}.");

        var knifePaint = Pick(knifePaints);
        var glove = Pick(_catalog.Gloves);

        return new BotCosmeticLoadout
        {
            Team = team,
            AgentModel = Pick(modelPool),
            MusicKit = preservedMusicKit ?? Pick(_catalog.MusicKits),
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
        if (loadout.Weapons.TryGetValue(defIndex, out var existing))
            return existing;
        if (!_catalog.TryGetWeapon(defIndex, out var weapon) || weapon.Paints.Count == 0)
            return null;

        var paint = Pick(weapon.Paints);
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

    private IReadOnlyList<StickerSelection> RollStickers(int schemaCount)
    {
        if (_catalog.StickerKits.Count == 0 || schemaCount <= 0)
            return Array.Empty<StickerSelection>();

        var count = _random.Next(MaximumStickers + 1);
        var stickers = new StickerSelection[count];
        for (var slot = 0; slot < count; slot++)
        {
            stickers[slot] = new StickerSelection(
                Pick(_catalog.StickerKits),
                slot,
                (uint)(slot % schemaCount));
        }
        return stickers;
    }

    private KeychainSelection? RollKeychain(ushort weaponDefIndex)
    {
        if (_catalog.KeychainDefinitions.Count == 0
            || _random.Next(KeychainChanceDenominator) >= KeychainChanceNumerator)
            return null;

        var definition = Pick(_catalog.KeychainDefinitions);
        var sticker = definition == StickerSlabDefinition
            ? Pick(_catalog.StickerKits)
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
}
