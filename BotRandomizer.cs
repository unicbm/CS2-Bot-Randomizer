using System.Runtime.InteropServices;
using BotRandomizer.API;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace BotRandomizer;

public sealed class BotRandomizerPlugin : BasePlugin
{
    private const int WeaponApplyRetryCount = 2;
    private const float WeaponApplyRetryDelay = 0.05f;

    private static readonly PluginCapability<IBotCosmeticOwnershipApi> OwnershipCapability =
        new(BotRandomizerApiContract.CapabilityName);

    private readonly CosmeticStateStore _states = new();
    private readonly CosmeticOwnershipService _ownership = new();
    private readonly RandomizerOptions _options = new();

    private CosmeticCatalog? _catalog;
    private CosmeticRoller? _roller;
    private CosmeticApplicator? _applicator;
    private bool _giveNamedItemHooked;

    public override string ModuleName => "BotRandomizer";
    public override string ModuleVersion => "1.3.2";
    public override string ModuleAuthor => "ed0ard, Misaka17032 & unicbm";
    public override string ModuleDescription =>
        "Stable per-bot knives, gloves, weapon skins, stickers, charms, agents and music kits";

    public override void Load(bool hotReload)
    {
        LoadCatalog();
        LoadAttributeWriter();

        _ownership.Changed += OnOwnershipChanged;
        OwnershipApiRegistry.SetCurrent(_ownership);
        if (OwnershipApiRegistry.TryMarkCapabilityRegistered())
        {
            Capabilities.RegisterPluginCapability(
                OwnershipCapability,
                OwnershipApiRegistry.GetCurrent);
        }

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventRoundMvp>(OnRoundMvp, HookMode.Pre);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        RegisterEventHandler<EventItemPickup>(OnItemPickup);
        AddTimer(1.0f, _ownership.CleanupExpired, TimerFlags.REPEAT);

        VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);
        _giveNamedItemHooked = true;

        if (hotReload)
            RestoreAllBots(CosmeticScope.All);
    }

    public override void Unload(bool hotReload)
    {
        if (_giveNamedItemHooked)
        {
            VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Post);
            _giveNamedItemHooked = false;
        }

        _ownership.Changed -= OnOwnershipChanged;
        OwnershipApiRegistry.ClearCurrent(_ownership);
        _ownership.Dispose();
    }

    private void LoadCatalog()
    {
        try
        {
            var path = Path.Combine(ModuleDirectory, "cosmetic_catalog.json");
            _catalog = CosmeticCatalog.Load(path);
            _roller = new CosmeticRoller(_catalog);
            Logger.LogInformation(
                "[BotRandomizer] Catalog {Commit}: {Weapons} weapons, {Paints} weapon paints, {Stickers} stickers, {Charms} charms",
                _catalog.SourceCommit[..12],
                _catalog.WeaponCount,
                _catalog.WeaponPaintCount,
                _catalog.StickerKits.Count,
                _catalog.KeychainDefinitions.Count);
        }
        catch (Exception exception)
        {
            _catalog = null;
            _roller = null;
            Logger.LogError(exception, "[BotRandomizer] cosmetic_catalog.json is invalid; randomization disabled");
        }
    }

    private void LoadAttributeWriter()
    {
        try
        {
            var writer = new MemoryFunctionWithReturn<nint, string, float, int>(
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? "55 48 89 E5 41 57 41 56 49 89 FE 41 55 41 54 53 48 89 F3 48 83 EC ? F3 0F 11 85"
                    : "40 53 55 41 56 48 81 EC ? ? ? ? 0F 29 74 24");
            _applicator = new CosmeticApplicator(writer, Logger);
        }
        catch (Exception exception)
        {
            _applicator = new CosmeticApplicator(null, Logger);
            Logger.LogError(
                exception,
                "[BotRandomizer] SetOrAddAttributeValueByName signature failed; economic cosmetics disabled");
        }
    }

    private void OnMapStart(string mapName)
    {
        _states.Reset();
        _ownership.Reset();
        _roller?.ResetMap();
        foreach (var model in RandomizerAssets.CounterTerroristModels)
            Server.PrecacheModel(model);
        foreach (var model in RandomizerAssets.TerroristModels)
            Server.PrecacheModel(model);
    }

    private void OnClientDisconnect(int playerSlot)
    {
        _states.Remove(playerSlot);
        _ownership.ReleaseSlot(playerSlot);
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var state = GetOrCreateState(@event.Userid);
        if (state is null || !_options.Enabled)
            return HookResult.Continue;

        var slot = state.Slot;
        var userId = state.UserId;
        var generation = state.Generation;
        Server.NextFrame(() =>
        {
            if (!TryResolveCurrentBot(slot, userId, generation, out var player, out var pawn, out var current))
                return;

            ApplyIdentity(player, pawn, current, CosmeticScope.Agent | CosmeticScope.MusicKit);
            ApplyWearables(player, pawn, current);
            ScheduleWearableRetry(slot, userId, generation, 0.10f);
            ScheduleWearableRetry(slot, userId, generation, 0.25f);
        });

        return HookResult.Continue;
    }

    private HookResult OnGiveNamedItemPost(DynamicHook hook)
    {
        if (!_options.Enabled || !_options.Weapons || _applicator is null || _roller is null)
            return HookResult.Continue;

        try
        {
            var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
            var weapon = hook.GetReturn<CBasePlayerWeapon>();
            var player = GetPlayerFromItemServices(itemServices);
            var state = GetOrCreateState(player);
            if (weapon is not { IsValid: true }
                || state is null
                || !_ownership.CanWrite(state.Slot, CosmeticScope.Weapons))
            {
                return HookResult.Continue;
            }

            // The returned weapon is not guaranteed to be fully owned/initialized while
            // GiveNamedItem's post hook is still on the native stack.
            var weaponEntityHandle = weapon.EntityHandle.Raw;
            if (weaponEntityHandle == Utilities.InvalidEHandleIndex)
                return HookResult.Continue;
            if (weapon.DesignerName.Contains("knife", StringComparison.Ordinal)
                || weapon.DesignerName == "weapon_bayonet")
            {
                return HookResult.Continue;
            }

            var slot = state.Slot;
            var userId = state.UserId;
            var generation = state.Generation;
            Server.NextFrame(() => TryApplyGivenWeapon(
                slot,
                userId,
                generation,
                weaponEntityHandle,
                WeaponApplyRetryCount));
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "[BotRandomizer] GiveNamedItem post-hook failed");
        }

        return HookResult.Continue;
    }

    private HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
    {
        if (!_options.Enabled || !_options.Knives || _applicator is null)
            return HookResult.Continue;
        if (string.IsNullOrEmpty(@event.Item)
            || (!@event.Item.Contains("knife", StringComparison.Ordinal)
                && !@event.Item.Contains("bayonet", StringComparison.Ordinal)))
        {
            return HookResult.Continue;
        }

        var state = GetOrCreateState(@event.Userid);
        if (state is null)
            return HookResult.Continue;

        ScheduleKnifeSync(state.Slot, state.UserId, state.Generation, nextFrame: true);
        ScheduleKnifeSync(state.Slot, state.UserId, state.Generation, delay: 0.10f);
        ScheduleKnifeSync(state.Slot, state.UserId, state.Generation, delay: 0.25f);
        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not { IsValid: true, IsBot: true } || player.UserId is not int userId)
            return HookResult.Continue;

        if (@event.Disconnect)
        {
            OnClientDisconnect(player.Slot);
            return HookResult.Continue;
        }

        if (_roller is null || !IsPlayableTeam(@event.Team))
        {
            _states.BumpGeneration(player.Slot);
            return HookResult.Continue;
        }

        _states.Reroll(
            player.Slot,
            userId,
            (byte)@event.Team,
            preserveMusic: true,
            music => _roller.RollLoadout((byte)@event.Team, music));
        return HookResult.Continue;
    }

    private HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        if (!_options.Enabled || !_options.Music)
            return HookResult.Continue;

        var state = GetOrCreateState(@event.Userid);
        var player = @event.Userid;
        if (state is null
            || player is null
            || !_ownership.CanWrite(state.Slot, CosmeticScope.MusicKit))
        {
            return HookResult.Continue;
        }

        ApplyMusicKit(player, state.Loadout.MusicKit, 0);
        @event.Musickitid = state.Loadout.MusicKit;
        @event.Musickitmvps = 0;
        @event.Nomusic = 0;
        return HookResult.Continue;
    }

    private SlotCosmeticState? GetOrCreateState(CCSPlayerController? player)
    {
        if (_roller is null
            || player is not { IsValid: true, IsBot: true, IsHLTV: false }
            || player.UserId is not int userId
            || !IsPlayableTeam(player.TeamNum))
        {
            return null;
        }

        var team = (byte)player.TeamNum;
        return _states.GetOrCreate(
            player.Slot,
            userId,
            team,
            music => _roller.RollLoadout(team, music));
    }

    private void ApplyIdentity(
        CCSPlayerController player,
        CCSPlayerPawn pawn,
        SlotCosmeticState state,
        CosmeticScope scope)
    {
        if (_applicator is null)
            return;

        if ((scope & CosmeticScope.Agent) != 0
            && _options.Agents
            && _ownership.CanWrite(state.Slot, CosmeticScope.Agent))
        {
            _applicator.ApplyAgent(pawn, state.Loadout.AgentModel);
        }

        if ((scope & CosmeticScope.MusicKit) != 0
            && _options.Music
            && _ownership.CanWrite(state.Slot, CosmeticScope.MusicKit))
        {
            ApplyMusicKit(player, state.Loadout.MusicKit, 0);
        }
    }

    private void ApplyWearables(
        CCSPlayerController player,
        CCSPlayerPawn pawn,
        SlotCosmeticState state,
        CosmeticScope scope = CosmeticScope.Knife | CosmeticScope.Gloves)
    {
        if (_applicator is null
            || player is not { IsValid: true, IsBot: true }
            || !pawn.IsValid)
        {
            return;
        }

        if ((scope & CosmeticScope.Knife) != 0
            && _options.Knives
            && _ownership.CanWrite(state.Slot, CosmeticScope.Knife))
        {
            _applicator.ApplyKnife(pawn, state.Loadout.Knife);
        }

        if ((scope & CosmeticScope.Gloves) != 0
            && _options.Gloves
            && _ownership.CanWrite(state.Slot, CosmeticScope.Gloves))
        {
            _applicator.ApplyGloves(pawn, state.Loadout.Glove);
            var generation = state.Generation;
            AddTimer(0.2f, () =>
            {
                if (TryResolveCurrentBot(
                        state.Slot,
                        state.UserId,
                        generation,
                        out _,
                        out var currentPawn,
                        out _)
                    && _options.Gloves
                    && _ownership.CanWrite(state.Slot, CosmeticScope.Gloves))
                {
                    _applicator.ShowGloves(currentPawn);
                }
            });
        }
    }

    private WeaponApplyResult ApplyRandomWeapon(SlotCosmeticState state, CBasePlayerWeapon weapon)
    {
        if (_roller is null || _applicator is null || !weapon.IsValid)
            return WeaponApplyResult.Pending;
        if (weapon.DesignerName.Contains("knife", StringComparison.Ordinal)
            || weapon.DesignerName == "weapon_bayonet")
        {
            return WeaponApplyResult.Unsupported;
        }

        var defIndex = weapon.AttributeManager?.Item?.ItemDefinitionIndex ?? 0;
        if (defIndex == 0)
            return WeaponApplyResult.Pending;

        var selection = _roller.GetOrCreateWeapon(state.Loadout, defIndex);
        if (selection is null)
            return WeaponApplyResult.Unsupported;

        return _applicator.ApplyWeapon(weapon, selection, _options.Stickers, _options.Charms)
            ? WeaponApplyResult.Applied
            : WeaponApplyResult.Pending;
    }

    private void TryApplyGivenWeapon(
        int slot,
        int userId,
        long generation,
        uint weaponEntityHandle,
        int retriesRemaining)
    {
        if (!_options.Enabled
            || !_options.Weapons
            || !_states.IsCurrent(slot, userId, generation)
            || !_ownership.CanWrite(slot, CosmeticScope.Weapons))
        {
            return;
        }

        if (TryResolveOwnedBotWeapon(
                slot,
                userId,
                generation,
                weaponEntityHandle,
                out var state,
                out var weapon))
        {
            var result = ApplyRandomWeapon(state, weapon);
            if (result is WeaponApplyResult.Applied or WeaponApplyResult.Unsupported)
                return;
        }

        if (retriesRemaining <= 0)
            return;

        AddTimer(
            WeaponApplyRetryDelay,
            () => TryApplyGivenWeapon(
                slot,
                userId,
                generation,
                weaponEntityHandle,
                retriesRemaining - 1));
    }

    private void ApplyAllWeapons(CCSPlayerPawn pawn, SlotCosmeticState state)
    {
        if (!_options.Weapons || !_ownership.CanWrite(state.Slot, CosmeticScope.Weapons))
            return;

        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons is null)
            return;

        foreach (var handle in weapons)
        {
            if (handle.Value is { IsValid: true } weapon)
                ApplyRandomWeapon(state, weapon);
        }
    }

    private void ScheduleWearableRetry(
        int slot,
        int userId,
        long generation,
        float delay,
        CosmeticScope scope = CosmeticScope.Knife | CosmeticScope.Gloves)
    {
        AddTimer(delay, () =>
        {
            if (TryResolveCurrentBot(slot, userId, generation, out var player, out var pawn, out var state))
                ApplyWearables(player, pawn, state, scope);
        });
    }

    private void ScheduleKnifeSync(
        int slot,
        int userId,
        long generation,
        float? delay = null,
        bool nextFrame = false)
    {
        void Callback()
        {
            if (_applicator is not null
                && _options.Knives
                && _ownership.CanWrite(slot, CosmeticScope.Knife)
                && TryResolveCurrentBot(slot, userId, generation, out _, out var pawn, out _))
            {
                _applicator.SyncPickedUpKnife(pawn);
            }
        }

        if (nextFrame)
            Server.NextFrame(Callback);
        else if (delay is float seconds)
            AddTimer(seconds, Callback);
    }

    private bool TryResolveCurrentBot(
        int slot,
        int userId,
        long generation,
        out CCSPlayerController player,
        out CCSPlayerPawn pawn,
        out SlotCosmeticState state)
    {
        player = null!;
        pawn = null!;
        state = null!;
        if (!_options.Enabled || !_states.IsCurrent(slot, userId, generation))
            return false;

        var resolved = Utilities.GetPlayerFromSlot(slot);
        if (resolved is not { IsValid: true, IsBot: true, IsHLTV: false }
            || resolved.UserId != userId
            || resolved.PlayerPawn?.Value is not { IsValid: true } resolvedPawn
            || !_states.TryGet(slot, out var resolvedState))
        {
            return false;
        }

        player = resolved;
        pawn = resolvedPawn;
        state = resolvedState;
        return true;
    }

    private bool TryResolveOwnedBotWeapon(
        int slot,
        int userId,
        long generation,
        uint weaponEntityHandle,
        out SlotCosmeticState state,
        out CBasePlayerWeapon weapon)
    {
        state = null!;
        weapon = null!;
        if (!TryResolveCurrentBot(
                slot,
                userId,
                generation,
                out var player,
                out var pawn,
                out var current)
            || !player.PawnIsAlive)
        {
            return false;
        }

        var candidate = new CHandle<CBasePlayerWeapon>(weaponEntityHandle).Value;
        if (candidate is not { IsValid: true } || !PawnOwnsWeapon(pawn, candidate))
            return false;

        state = current;
        weapon = candidate;
        return true;
    }

    private static bool PawnOwnsWeapon(CCSPlayerPawn pawn, CBasePlayerWeapon weapon)
    {
        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons is null)
            return false;

        foreach (var handle in weapons)
        {
            if (handle.Value is { IsValid: true } candidate && candidate.Handle == weapon.Handle)
                return true;
        }

        return false;
    }

    private void OnOwnershipChanged(OwnershipChange change)
        => Server.NextFrame(() => HandleOwnershipChanged(change));

    private void HandleOwnershipChanged(OwnershipChange change)
    {
        _states.BumpGeneration(change.PlayerSlot);
        if ((change.Kind is OwnershipChangeKind.Released or OwnershipChangeKind.Expired)
            && change.ReleaseMode != CosmeticReleaseMode.LeaveCurrent)
        {
            RestoreBot(change.PlayerSlot, change.Scope, change.ReleaseMode);
        }
    }

    private void RestoreBot(
        int slot,
        CosmeticScope scope,
        CosmeticReleaseMode mode = CosmeticReleaseMode.RestoreBaseline)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (_roller is null
            || player is not { IsValid: true, IsBot: true, IsHLTV: false }
            || player.UserId is not int userId
            || !IsPlayableTeam(player.TeamNum))
        {
            return;
        }

        SlotCosmeticState? state;
        if (mode == CosmeticReleaseMode.Reroll)
        {
            var team = (byte)player.TeamNum;
            state = _states.Reroll(
                slot,
                userId,
                team,
                preserveMusic: false,
                music => _roller.RollLoadout(team, music));
        }
        else
        {
            state = GetOrCreateState(player);
        }

        if (state is null)
            return;

        var generation = state.Generation;
        Server.NextFrame(() =>
        {
            if (!TryResolveCurrentBot(slot, userId, generation, out var currentPlayer, out var pawn, out var current))
                return;

            ApplyIdentity(currentPlayer, pawn, current, scope);
            var wearableScope = scope & (CosmeticScope.Knife | CosmeticScope.Gloves);
            if (wearableScope != CosmeticScope.None)
            {
                ApplyWearables(currentPlayer, pawn, current, wearableScope);
                ScheduleWearableRetry(slot, userId, generation, 0.10f, wearableScope);
                ScheduleWearableRetry(slot, userId, generation, 0.25f, wearableScope);
            }
            if ((scope & CosmeticScope.Weapons) != 0)
                ApplyAllWeapons(pawn, current);
        });
    }

    private void RestoreAllBots(CosmeticScope scope)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player is { IsValid: true, IsBot: true, IsHLTV: false })
                RestoreBot(player.Slot, scope);
        }
    }

    [ConsoleCommand("br_status", "Show BotRandomizer runtime status")]
    public void OnStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand(
            $"BotRandomizer version={ModuleVersion} api={BotRandomizerApiContract.Major}.{BotRandomizerApiContract.Minor} "
            + $"enabled={Format(_options.Enabled)} native={Format(_applicator?.NativeAvailable == true)} "
            + $"catalog={(_catalog is null ? "invalid" : _catalog.SourceCommit[..12])}");
        command.ReplyToCommand(
            $"weapons={Format(_options.Weapons)} knives={Format(_options.Knives)} "
            + $"gloves={Format(_options.Gloves)} agents={Format(_options.Agents)} "
            + $"music={Format(_options.Music)} stickers={Format(_options.Stickers)} "
            + $"charms={Format(_options.Charms)} states={_states.States.Count}");
    }

    [ConsoleCommand("br_set", "Set a BotRandomizer runtime option")]
    [RequiresPermissions("@css/cvar")]
    public void OnSetCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount != 3 || !TryParseBoolean(command.GetArg(2), out var value))
        {
            command.ReplyToCommand(
                "Usage: br_set <enabled|weapons|knives|gloves|agents|music|stickers|charms> <on|off>");
            return;
        }

        var scope = command.GetArg(1).ToLowerInvariant() switch
        {
            "enabled" => SetOption(() => _options.Enabled = value, CosmeticScope.All),
            "weapons" => SetOption(() => _options.Weapons = value, CosmeticScope.Weapons),
            "knives" => SetOption(() => _options.Knives = value, CosmeticScope.Knife),
            "gloves" => SetOption(() => _options.Gloves = value, CosmeticScope.Gloves),
            "agents" => SetOption(() => _options.Agents = value, CosmeticScope.Agent),
            "music" => SetOption(() => _options.Music = value, CosmeticScope.MusicKit),
            "stickers" => SetOption(() => _options.Stickers = value, CosmeticScope.Weapons),
            "charms" => SetOption(() => _options.Charms = value, CosmeticScope.Weapons),
            _ => CosmeticScope.None
        };

        if (scope == CosmeticScope.None)
        {
            command.ReplyToCommand("Unknown BotRandomizer option.");
            return;
        }

        command.ReplyToCommand($"{command.GetArg(1)}={Format(value)}");
        if (_options.Enabled && (value || scope == CosmeticScope.Weapons))
            RestoreAllBots(scope);
    }

    [ConsoleCommand("br_reroll", "Reroll all bots or one bot slot")]
    [RequiresPermissions("@css/cvar")]
    public void OnRerollCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_roller is null)
        {
            command.ReplyToCommand("Cosmetic catalog is unavailable.");
            return;
        }

        var target = command.ArgCount >= 2 ? command.GetArg(1) : "all";
        var rerolled = 0;
        foreach (var bot in Utilities.GetPlayers())
        {
            if (bot is not { IsValid: true, IsBot: true, IsHLTV: false }
                || bot.UserId is not int userId
                || !IsPlayableTeam(bot.TeamNum)
                || (target != "all" && (!int.TryParse(target, out var slot) || bot.Slot != slot)))
            {
                continue;
            }

            var team = (byte)bot.TeamNum;
            _states.Reroll(
                bot.Slot,
                userId,
                team,
                preserveMusic: false,
                music => _roller.RollLoadout(team, music));
            RestoreBot(bot.Slot, CosmeticScope.All);
            rerolled++;
        }

        command.ReplyToCommand($"Rerolled {rerolled} bot loadout(s).");
    }

    [ConsoleCommand("br_ownership", "Show active cosmetic ownership leases")]
    [RequiresPermissions("@css/cvar")]
    public void OnOwnershipCommand(CCSPlayerController? player, CommandInfo command)
    {
        var statuses = _ownership.GetAllStatuses();
        if (statuses.Count == 0)
        {
            command.ReplyToCommand("No active external cosmetic leases.");
            return;
        }

        foreach (var status in statuses)
        {
            command.ReplyToCommand(
                $"slot={status.PlayerSlot} owner={status.Owner} lease={status.LeaseId} "
                + $"scope={status.Scope} purpose={status.Purpose} expires={status.ExpiresAt:O}");
        }
    }

    private CosmeticScope SetOption(Action setter, CosmeticScope scope)
    {
        setter();
        foreach (var state in _states.States)
            _states.BumpGeneration(state.Slot);
        return scope;
    }

    private static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
    {
        var pawn = itemServices.Pawn.Value;
        if (pawn is not { IsValid: true } || pawn.Controller.Value is not { IsValid: true } controller)
            return null;

        var player = new CCSPlayerController(controller.Handle);
        return player.IsValid ? player : null;
    }

    private static bool IsPlayableTeam(int team)
        => team is RandomizerAssets.TerroristTeam or RandomizerAssets.CounterTerroristTeam;

    private static bool TryParseBoolean(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "1":
            case "on":
            case "true":
            case "yes":
                result = true;
                return true;
            case "0":
            case "off":
            case "false":
            case "no":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static string Format(bool value) => value ? "on" : "off";

    private static void ApplyMusicKit(CCSPlayerController player, int kitId, int musicKitMvps)
    {
        var inventory = player.InventoryServices;
        if (inventory is not null)
        {
            inventory.MusicID = checked((ushort)kitId);
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
        }

        player.MusicKitID = kitId;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_iMusicKitID");
        player.MusicKitMVPs = musicKitMvps;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_iMusicKitMVPs");
        player.MvpNoMusic = false;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_bMvpNoMusic");
    }

    private enum WeaponApplyResult
    {
        Applied,
        Pending,
        Unsupported
    }
}
