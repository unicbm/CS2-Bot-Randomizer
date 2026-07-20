namespace BotRandomizer;

internal sealed record PaintCatalogEntry(
    int PaintKit,
    bool Legacy,
    float WearMin,
    float WearMax);

internal sealed record WeaponCatalogEntry(
    string DesignerName,
    ushort DefIndex,
    int StickerSchemaCount,
    int LegacyStickerSchemaCount,
    IReadOnlyList<PaintCatalogEntry> Paints);

internal sealed record GloveCatalogEntry(
    ushort DefIndex,
    int PaintKit,
    float WearMin,
    float WearMax);

internal readonly record struct KnifeDefinition(
    string DesignerName,
    ushort DefIndex,
    string ModelPath);

internal sealed record StickerSelection(
    uint DefIndex,
    int Slot,
    uint Schema,
    float Wear = 0.0f,
    float? Rotation = null,
    float? X = null,
    float? Y = null);

internal readonly record struct CharmPlacement(float X, float Y, float Z);

internal sealed record KeychainSelection(
    uint DefIndex,
    int Seed,
    int Slot = 0,
    uint? Sticker = null,
    float? X = null,
    float? Y = null,
    float? Z = null);

internal sealed record WeaponCosmeticSelection(
    int PaintKit,
    int Seed,
    float Wear,
    bool Legacy,
    IReadOnlyList<StickerSelection> Stickers,
    KeychainSelection? Keychain);

internal sealed record KnifeSelection(ushort DefIndex, int PaintKit, float Wear);

internal sealed record GloveSelection(ushort DefIndex, int PaintKit, float Wear);

internal sealed class BotCosmeticLoadout
{
    public required byte Team { get; init; }
    public required string AgentModel { get; init; }
    public required int MusicKit { get; init; }
    public required KnifeSelection Knife { get; init; }
    public required GloveSelection Glove { get; init; }
    public Dictionary<ushort, WeaponCosmeticSelection> Weapons { get; } = new();
}

internal sealed class RandomizerOptions
{
    public bool Enabled { get; set; } = true;
    public bool Weapons { get; set; } = true;
    public bool Knives { get; set; } = true;
    public bool Gloves { get; set; } = true;
    public bool Agents { get; set; } = true;
    public bool Music { get; set; } = true;
    public bool Stickers { get; set; } = true;
    public bool Charms { get; set; } = true;
}
