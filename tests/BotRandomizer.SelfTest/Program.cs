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
Assert(catalog.SourceRepository == "ianlucas/cs2-lib", "catalog source");
Assert(catalog.WeaponCount == 35, "weapon count");
Assert(catalog.WeaponPaintCount == 1456, "weapon paint count");
Assert(catalog.KnifePaintCount == 556, "knife paint count");
Assert(catalog.Gloves.Count == 94, "glove count");
Assert(catalog.StickerKits.Count == 10565, "sticker count");
Assert(catalog.KeychainDefinitions.Count == 81, "keychain count");
Assert(catalog.MusicKits.Count == 98, "music kit count");
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

var roller = new CosmeticRoller(catalog, charmPlacements, new Random(1979));
var allWeaponsLoadout = roller.RollLoadout(RandomizerAssets.TerroristTeam);
foreach (var weaponEntry in catalog.Weapons)
{
    Assert(roller.GetOrCreateWeapon(allWeaponsLoadout, weaponEntry.DefIndex) is not null,
        $"weapon {weaponEntry.DefIndex} roll");
}

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

var charmRoller = new CosmeticRoller(catalog, charmPlacements, new Random(20260720));
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

var defaultPlacementRoller = new CosmeticRoller(catalog, charmPlacements, new Random(20260721));
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
