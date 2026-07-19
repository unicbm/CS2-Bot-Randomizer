using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace BotRandomizer;

internal sealed class CosmeticApplicator
{
    private const ulong MinimumCustomItemId = 65155030971;

    private readonly MemoryFunctionWithReturn<nint, string, float, int>? _setAttributeByName;
    private readonly ILogger _logger;
    private ulong _nextItemId = MinimumCustomItemId;
    private bool _weaponErrorLogged;

    internal CosmeticApplicator(
        MemoryFunctionWithReturn<nint, string, float, int>? setAttributeByName,
        ILogger logger)
    {
        _setAttributeByName = setAttributeByName;
        _logger = logger;
    }

    internal bool NativeAvailable => _setAttributeByName is not null;

    internal void ApplyAgent(CCSPlayerPawn pawn, string model)
    {
        if (!pawn.IsValid)
            return;

        pawn.SetModel(model);
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CBodyComponent");
        var color = pawn.Render;
        pawn.Render = Color.FromArgb(255, color.R, color.G, color.B);
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
    }

    internal void ApplyWeapon(
        CBasePlayerWeapon weapon,
        WeaponCosmeticSelection selection,
        bool includeStickers,
        bool includeCharms)
    {
        if (_setAttributeByName is null || !weapon.IsValid)
            return;

        try
        {
            var item = weapon.AttributeManager?.Item;
            if (item is null)
                return;

            var attributeList = item.AttributeList;
            var networkedAttributes = item.NetworkedDynamicAttributes;
            if (attributeList.Handle == IntPtr.Zero || networkedAttributes.Handle == IntPtr.Zero)
                return;

            attributeList.Attributes.RemoveAll();
            networkedAttributes.Attributes.RemoveAll();
            AssignItemId(item);
            // Initialized is for an econ view supplied before GiveNamedItem constructs
            // the entity; changing it on a live returned weapon is not lifecycle-safe.

            weapon.FallbackPaintKit = selection.PaintKit;
            weapon.FallbackSeed = selection.Seed;
            weapon.FallbackWear = selection.Wear;
            SetTextureAttributes(
                networkedAttributes,
                attributeList,
                selection.PaintKit,
                selection.Seed,
                selection.Wear);

            if (includeStickers)
            {
                foreach (var sticker in selection.Stickers)
                    SetStickerAttributes(networkedAttributes, sticker);
            }

            if (includeCharms && selection.Keychain is not null)
            {
                SetKeychainAttributes(networkedAttributes, selection.Keychain);
                SetKeychainAttributes(attributeList, selection.Keychain);
            }

            Utilities.SetStateChanged(weapon, "CEconEntity", "m_AttributeManager");
            weapon.AcceptInput("SetBodygroup", value: $"body,{(selection.Legacy ? 1 : 0)}");
        }
        catch (Exception exception)
        {
            if (_weaponErrorLogged)
                return;

            _weaponErrorLogged = true;
            _logger.LogError(exception, "[BotRandomizer] Failed to apply weapon cosmetics");
        }
    }

    internal void ApplyKnife(CCSPlayerPawn pawn, KnifeSelection selection)
    {
        if (!pawn.IsValid)
            return;

        try
        {
            var weapons = pawn.WeaponServices?.MyWeapons;
            if (weapons is null)
                return;

            foreach (var handle in weapons)
            {
                var weapon = handle.Value;
                if (weapon is not { IsValid: true })
                    continue;
                if (weapon.DesignerName is not ("weapon_knife" or "weapon_knife_t"))
                    continue;

                weapon.AcceptInput("ChangeSubclass", value: selection.DefIndex.ToString());
                var item = weapon.AttributeManager?.Item;
                if (item is null)
                    return;

                item.ItemDefinitionIndex = selection.DefIndex;
                item.EntityQuality = 3;
                if (_setAttributeByName is not null)
                {
                    item.AttributeList.Attributes.RemoveAll();
                    item.NetworkedDynamicAttributes.Attributes.RemoveAll();
                    AssignItemId(item);
                    weapon.FallbackPaintKit = selection.PaintKit;
                    weapon.FallbackSeed = 0;
                    weapon.FallbackWear = selection.Wear;
                    SetTextureAttributes(
                        item.NetworkedDynamicAttributes,
                        item.AttributeList,
                        selection.PaintKit,
                        0,
                        selection.Wear);
                }
                Utilities.SetStateChanged(weapon, "CEconEntity", "m_AttributeManager");
                return;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[BotRandomizer] Failed to apply knife cosmetics");
        }
    }

    internal void ApplyGloves(CCSPlayerPawn pawn, GloveSelection selection)
    {
        if (_setAttributeByName is null || !pawn.IsValid)
            return;

        try
        {
            var item = pawn.EconGloves;
            item.NetworkedDynamicAttributes.Attributes.RemoveAll();
            item.AttributeList.Attributes.RemoveAll();
            item.ItemDefinitionIndex = selection.DefIndex;
            AssignItemId(item);
            SetTextureAttributes(
                item.NetworkedDynamicAttributes,
                item.AttributeList,
                selection.PaintKit,
                0,
                selection.Wear);
            item.Initialized = true;
            pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,0");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[BotRandomizer] Failed to apply glove cosmetics");
        }
    }

    internal void ShowGloves(CCSPlayerPawn pawn)
    {
        if (pawn.IsValid)
            pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,1");
    }

    internal void SyncPickedUpKnife(CCSPlayerPawn pawn)
    {
        if (!pawn.IsValid)
            return;

        try
        {
            var weapons = pawn.WeaponServices?.MyWeapons;
            if (weapons is null)
                return;

            foreach (var handle in weapons)
            {
                var weapon = handle.Value;
                if (weapon is not { IsValid: true }
                    || !RandomizerAssets.KnifeDefIndexByName.TryGetValue(
                        weapon.DesignerName,
                        out var defIndex))
                {
                    continue;
                }

                var item = weapon.AttributeManager?.Item;
                if (item is null)
                    continue;

                weapon.AcceptInput("ChangeSubclass", value: defIndex.ToString());
                item.ItemDefinitionIndex = defIndex;
                Utilities.SetStateChanged(weapon, "CEconEntity", "m_AttributeManager");
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[BotRandomizer] Failed to synchronize a picked-up knife");
        }
    }

    private void SetTextureAttributes(
        CAttributeList networkedAttributes,
        CAttributeList attributeList,
        int paintKit,
        int seed,
        float wear)
    {
        SetAttribute(networkedAttributes, "set item texture prefab", paintKit);
        SetAttribute(networkedAttributes, "set item texture seed", seed);
        SetAttribute(networkedAttributes, "set item texture wear", wear);
        SetAttribute(attributeList, "set item texture prefab", paintKit);
        SetAttribute(attributeList, "set item texture seed", seed);
        SetAttribute(attributeList, "set item texture wear", wear);
    }

    private void SetStickerAttributes(CAttributeList attributes, StickerSelection sticker)
    {
        var slot = $"sticker slot {sticker.Slot}";
        SetAttribute(attributes, $"{slot} id", AttributeEncoding.UInt32BitsToSingle(sticker.DefIndex));
        SetAttribute(attributes, $"{slot} schema", AttributeEncoding.UInt32BitsToSingle(sticker.Schema));
        SetAttribute(attributes, $"{slot} wear", sticker.Wear);
        if (sticker.Rotation is float rotation)
            SetAttribute(attributes, $"{slot} rotation", rotation);
        if (sticker.X is float x)
            SetAttribute(attributes, $"{slot} offset x", x);
        if (sticker.Y is float y)
            SetAttribute(attributes, $"{slot} offset y", y);
    }

    private void SetKeychainAttributes(CAttributeList attributes, KeychainSelection keychain)
    {
        var slot = $"keychain slot {keychain.Slot}";
        SetAttribute(attributes, $"{slot} id", AttributeEncoding.UInt32BitsToSingle(keychain.DefIndex));
        SetAttribute(attributes, $"{slot} seed", AttributeEncoding.Int32BitsToSingle(keychain.Seed));
        if (keychain.Sticker is uint sticker)
            SetAttribute(attributes, $"{slot} sticker", AttributeEncoding.UInt32BitsToSingle(sticker));
        if (keychain.X is float x)
            SetAttribute(attributes, $"{slot} offset x", x);
        if (keychain.Y is float y)
            SetAttribute(attributes, $"{slot} offset y", y);
        if (keychain.Z is float z)
            SetAttribute(attributes, $"{slot} offset z", z);
    }

    private void SetAttribute(CAttributeList attributes, string name, float value)
    {
        if (_setAttributeByName is not null && attributes.Handle != IntPtr.Zero)
            _setAttributeByName.Invoke(attributes.Handle, name, value);
    }

    private void AssignItemId(CEconItemView item)
    {
        var itemId = unchecked(_nextItemId++);
        item.ItemID = itemId;
        item.ItemIDLow = (uint)(itemId & uint.MaxValue);
        item.ItemIDHigh = (uint)(itemId >> 32);
    }
}
