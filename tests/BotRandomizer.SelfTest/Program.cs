using BotRandomizer;
using BotRandomizer.API;

if (args.Length != 1)
    throw new InvalidOperationException("Pass the absolute path to cosmetic_catalog.json.");

var catalog = CosmeticCatalog.Load(args[0]);
var placementPath = Path.Combine(
    Path.GetDirectoryName(Path.GetFullPath(args[0]))
        ?? throw new InvalidOperationException("Catalog path has no directory."),
    "charm_placements.json");
var charmPlacements = CharmPlacementCatalog.Load(placementPath, catalog);
var assetCatalog = RandomizerAssetCatalog.Load(args[0], catalog);
var configPath = Path.Combine(
    Path.GetDirectoryName(Path.GetFullPath(args[0]))
        ?? throw new InvalidOperationException("Catalog path has no directory."),
    "randomizer_config.json");
var config = RandomizerConfig.Load(configPath, catalog, assetCatalog);
Assert(catalog.SourceRepository == "ianlucas/cs2-lib", "catalog source");
Assert(assetCatalog.Knives.Count == 20, "configurable knife type count");
Assert(assetCatalog.CounterTerroristAgents.Count > 0, "Counter-Terrorist agent assets");
Assert(assetCatalog.TerroristAgents.Count > 0, "Terrorist agent assets");
Assert(catalog.WeaponCount == 35, "weapon count");
Assert(catalog.WeaponPaintCount == 1456, "weapon paint count");
Assert(catalog.KnifePaintCount == 556, "knife paint count");
Assert(catalog.Gloves.Count == 94, "glove count");
Assert(catalog.StickerKits.Count == 10565, "sticker count");
Assert(catalog.KeychainDefinitions.Count == 81, "keychain count");
Assert(catalog.MusicKits.Count == 98, "music kit count");
Assert(charmPlacements.SourceDemoCount == 20, "charm placement parsed demo count");
Assert(charmPlacements.ContributingDemoCount == 19, "charm placement contributing demo count");
Assert(charmPlacements.WeaponCount == 16, "charm placement weapon count");
Assert(charmPlacements.PlacementCount == 158, "charm placement count");
Assert(charmPlacements.TryGetPlacements(7, out var akPlacements) && akPlacements.Count == 36,
    "AK-47 charm placement pool");
foreach (var defIndex in new ushort[] { 16, 23, 26, 60 })
{
    Assert(catalog.TryGetWeapon(defIndex, out var weapon) && weapon.Paints.Count > 0,
        $"BotBuy CT replacement weapon {defIndex}");
}
foreach (var (designerName, defIndex) in new (string, ushort)[]
{
    ("weapon_m4a1", 16),
    ("weapon_mp5sd", 23),
    ("weapon_bizon", 26),
    ("weapon_m4a1_silencer", 60)
})
{
    Assert(catalog.TryGetWeapon(designerName, out var weapon) && weapon.DefIndex == defIndex,
        $"GiveNamedItem mapping {designerName}");
}
Assert(catalog.Weapons.All(weapon => weapon.DesignerName.StartsWith("weapon_", StringComparison.Ordinal)),
    "weapon designer names");

Assert(BitConverter.SingleToInt32Bits(AttributeEncoding.UInt32BitsToSingle(0xDEADBEEF))
    == unchecked((int)0xDEADBEEF), "uint attribute bit encoding");
Assert(BitConverter.SingleToInt32Bits(AttributeEncoding.Int32BitsToSingle(-1234567))
    == -1234567, "int attribute bit encoding");
var itemIds = Enumerable.Range(0, 32).Select(_ => EconItemIdAllocator.Next()).ToArray();
Assert(itemIds.Distinct().Count() == itemIds.Length, "custom item IDs are process-unique");

var wearAllocator = new WeaponWearAllocator();
var paint = new PaintCatalogEntry(7, false, 0.0f, 1.0f);
var firstStickers = new[] { new StickerSelection(1, 0, 0) };
var secondStickers = new[] { new StickerSelection(2, 0, 0) };
var firstWear = wearAllocator.Reserve(7, paint, firstStickers);
var repeatedWear = wearAllocator.Reserve(7, paint, firstStickers);
var secondWear = wearAllocator.Reserve(7, paint, secondStickers);
Assert(firstWear == repeatedWear, "identical sticker signatures reuse wear");
Assert(firstWear != secondWear, "different sticker signatures reserve unique wear");

var roller = new CosmeticRoller(catalog, charmPlacements, assetCatalog, config, new Random(1979));
Assert(roller.KnifeTypeCount == 20, "default config enables every catalog knife family");
var allWeaponsLoadout = roller.RollLoadout(RandomizerAssets.TerroristTeam);
foreach (var weaponEntry in catalog.Weapons)
{
    Assert(roller.GetOrCreateWeapon(allWeaponsLoadout, weaponEntry.DefIndex) is not null,
        $"weapon {weaponEntry.DefIndex} roll");
}
var persistentAk = roller.GetOrCreateWeapon(allWeaponsLoadout, 7);
Assert(
    ReferenceEquals(persistentAk, roller.GetOrCreateWeapon(allWeaponsLoadout, 7)),
    "persistent mode reuses the same per-bot weapon combination");

var kaleidoscopeConfig = new RandomizerConfig
{
    Options = new RandomizerOptions
    {
        WeaponMode = WeaponRandomizationMode.Kaleidoscope
    }
};
kaleidoscopeConfig.Validate(catalog, assetCatalog);
var kaleidoscopeRoller = new CosmeticRoller(
    catalog,
    charmPlacements,
    assetCatalog,
    kaleidoscopeConfig,
    new Random(20260724));
var kaleidoscopeLoadout = kaleidoscopeRoller.RollLoadout(RandomizerAssets.TerroristTeam);
var kaleidoscopeFirst = kaleidoscopeRoller.GetOrCreateWeapon(kaleidoscopeLoadout, 7);
var kaleidoscopeSecond = kaleidoscopeRoller.GetOrCreateWeapon(kaleidoscopeLoadout, 7);
Assert(kaleidoscopeFirst is not null && kaleidoscopeSecond is not null,
    "kaleidoscope weapon rolls exist");
Assert(!ReferenceEquals(kaleidoscopeFirst, kaleidoscopeSecond),
    "kaleidoscope mode constructs a fresh combination for every weapon");
Assert(kaleidoscopeLoadout.Weapons.Count == 0,
    "kaleidoscope mode does not persist weapon combinations");

var stableStore = new CosmeticStateStore();
var stableState = stableStore.GetOrCreate(
    4,
    400,
    RandomizerAssets.TerroristTeam,
    music => roller.RollLoadout(RandomizerAssets.TerroristTeam, music));
var stableKnife = stableState.Loadout.Knife;
var stableGlove = stableState.Loadout.Glove;
roller.GetOrCreateWeapon(stableState.Loadout, 7);
var explicitReroll = stableStore.Reroll(
    4,
    400,
    RandomizerAssets.TerroristTeam,
    preserveMusic: false,
    music => roller.RollLoadout(RandomizerAssets.TerroristTeam, music))
    ?? throw new InvalidOperationException("stable reroll missing");
Assert(explicitReroll.Loadout.Knife == stableKnife && explicitReroll.Loadout.Glove == stableGlove,
    "explicit reroll preserves stable knife and glove identity");
var changedTeam = stableStore.GetOrCreate(
    4,
    400,
    RandomizerAssets.CounterTerroristTeam,
    music => roller.RollLoadout(RandomizerAssets.CounterTerroristTeam, music));
Assert(changedTeam.Loadout.Knife == stableKnife && changedTeam.Loadout.Glove == stableGlove,
    "team change preserves stable knife and glove identity");
roller.GetOrCreateWeapon(changedTeam.Loadout, 7);
stableStore.InvalidateWeaponSelections();
Assert(changedTeam.Loadout.Weapons.Count == 0,
    "config reload invalidates guns without touching stable wearables");
Assert(changedTeam.Loadout.Knife == stableKnife && changedTeam.Loadout.Glove == stableGlove,
    "config reload preserves stable knife and glove identity");

var sawStickers = false;
var sawKeychain = false;
var sawStickerSlab = false;
for (var iteration = 0; iteration < 250; iteration++)
{
    var loadout = roller.RollLoadout(RandomizerAssets.TerroristTeam);
    var weapon = roller.GetOrCreateWeapon(loadout, 7)
        ?? throw new InvalidOperationException("AK-47 roll missing.");
    var weaponCatalog = catalog.TryGetWeapon(7, out var entry)
        ? entry
        : throw new InvalidOperationException("AK-47 catalog missing.");
    var schemaCount = weapon.Legacy
        ? weaponCatalog.LegacyStickerSchemaCount
        : weaponCatalog.StickerSchemaCount;

    Assert(weapon.Stickers.Count <= 5, "sticker stack limit");
    for (var slot = 0; slot < weapon.Stickers.Count; slot++)
    {
        var sticker = weapon.Stickers[slot];
        Assert(sticker.Slot == slot, "contiguous sticker slots");
        Assert(sticker.Schema < schemaCount, "sticker schema range");
        Assert(catalog.StickerKits.Contains(sticker.DefIndex), "sticker definition catalog membership");
        sawStickers = true;
    }

    if (weapon.Keychain is { } keychain)
    {
        Assert(keychain.Seed is >= 1 and <= 100000, "keychain seed range");
        Assert(catalog.KeychainDefinitions.Contains(keychain.DefIndex), "keychain catalog membership");
        Assert(keychain.DefIndex == 37 ? keychain.Sticker is not null : keychain.Sticker is null,
            "Sticker Slab payload");
        Assert(keychain.X is float x
            && keychain.Y is float y
            && keychain.Z is float z
            && akPlacements.Contains(new CharmPlacement(x, y, z)),
            "weapon-specific charm placement");
        sawKeychain = true;
        sawStickerSlab |= keychain.DefIndex == 37;
    }
}
Assert(sawStickers, "sticker rolling exercised");
Assert(sawKeychain, "keychain rolling exercised");
Assert(sawStickerSlab, "Sticker Slab rolling exercised");

var charmRoller = new CosmeticRoller(
    catalog,
    charmPlacements,
    assetCatalog,
    config,
    new Random(20260720));
var charmCount = 0;
const int charmTrials = 10000;
for (var iteration = 0; iteration < charmTrials; iteration++)
{
    var loadout = charmRoller.RollLoadout(RandomizerAssets.TerroristTeam);
    var weapon = charmRoller.GetOrCreateWeapon(loadout, 7)
        ?? throw new InvalidOperationException("AK-47 probability roll missing.");
    if (weapon.Keychain is not null)
        charmCount++;
}
Assert(charmCount is >= 6800 and <= 7200, "70% keychain probability");

var defaultPlacementRoller = new CosmeticRoller(
    catalog,
    charmPlacements,
    assetCatalog,
    config,
    new Random(20260721));
var sawDefaultPlacementCharm = false;
for (var iteration = 0; iteration < 100; iteration++)
{
    var loadout = defaultPlacementRoller.RollLoadout(RandomizerAssets.CounterTerroristTeam);
    var weapon = defaultPlacementRoller.GetOrCreateWeapon(loadout, 23)
        ?? throw new InvalidOperationException("MP5-SD default placement roll missing.");
    if (weapon.Keychain is not { } keychain)
        continue;

    Assert(keychain.X is null && keychain.Y is null && keychain.Z is null,
        "unobserved weapon preserves CS2 default charm placement");
    sawDefaultPlacementCharm = true;
    break;
}
Assert(sawDefaultPlacementCharm, "unobserved weapon charm rolling exercised");

var knifeDenyConfig = new RandomizerConfig
{
    Filters = new RandomizerFilters
    {
        KnifeTypes = new SelectionFilter<ushort>
        {
            Mode = FilterMode.Deny,
            Items = [506, 512]
        }
    }
};
knifeDenyConfig.Validate(catalog, assetCatalog);
var knifeDenyRoller = new CosmeticRoller(
    catalog,
    charmPlacements,
    assetCatalog,
    knifeDenyConfig,
    new Random(20260722));
Assert(knifeDenyRoller.KnifeTypeCount == 18, "knife type deny-list removes whole knife families");
for (var iteration = 0; iteration < 250; iteration++)
{
    var knife = knifeDenyRoller.RollLoadout(RandomizerAssets.TerroristTeam).Knife;
    Assert(knife.DefIndex is not (506 or 512), "denied knife type never rolls");
}

var paintAllowConfig = new RandomizerConfig
{
    Filters = new RandomizerFilters
    {
        WeaponPaints = new SelectionFilter<CosmeticVariantKey>
        {
            Mode = FilterMode.Allow,
            Items = [new CosmeticVariantKey(7, 302)]
        }
    }
};
paintAllowConfig.Validate(catalog, assetCatalog);
var paintAllowRoller = new CosmeticRoller(
    catalog,
    charmPlacements,
    assetCatalog,
    paintAllowConfig,
    new Random(20260723));
var paintAllowLoadout = paintAllowRoller.RollLoadout(RandomizerAssets.TerroristTeam);
Assert(
    paintAllowRoller.GetOrCreateWeapon(paintAllowLoadout, 7)?.PaintKit == 302,
    "weapon paint allow-list selects the configured paint");
Assert(
    paintAllowRoller.GetOrCreateWeapon(paintAllowLoadout, 9) is null,
    "weapon paint allow-list leaves unlisted weapons untouched");

var now = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
using var ownership = new CosmeticOwnershipService(() => now);
var ownershipChanges = new List<OwnershipChange>();
ownership.Changed += ownershipChanges.Add;
var lease = ownership.AcquireLease(
    "SelfTest",
    3,
    CosmeticScope.Weapons | CosmeticScope.Knife,
    CosmeticLeasePurpose.Replay,
    ttlSeconds: 5);
Assert(lease.Acquired, "lease acquisition");
Assert(!ownership.CanWrite(3, CosmeticScope.Weapons), "leased weapon scope is blocked");
Assert(ownership.CanWrite(3, CosmeticScope.Agent), "unleased agent scope remains writable");
Assert(ownership.RenewLease("SelfTest", 3, lease.LeaseId, 5), "lease renewal");
now = now.AddSeconds(6);
ownership.CleanupExpired();
Assert(ownership.CanWrite(3, CosmeticScope.Weapons), "expired lease restores writes");
Assert(ownershipChanges.Any(change => change.Kind == OwnershipChangeKind.Expired), "expiry notification");

Console.WriteLine("BotRandomizer self-test passed.");

static void Assert(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException($"Self-test failed: {label}");
}
