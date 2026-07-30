using BotRandomizerApi;
using CounterStrikeSharp.API;

namespace BotRandomizer;

public sealed partial class BotRandomizerPlugin
{
    private sealed class BotRandomizerApiFacade : IBotRandomizerApi
    {
        private readonly BotRandomizerPlugin _plugin;

        internal BotRandomizerApiFacade(BotRandomizerPlugin plugin)
        {
            _plugin = plugin;
        }

        public int ApiVersion => BotRandomizerContract.ApiVersion;

        public BotRandomizerProviderInfo GetProviderInfo()
            => _plugin.GetProviderInfoForApi();

        public bool TryGetManagedBot(int slot, out BotRandomizerManagedBot state)
            => _plugin.TryGetManagedBotForApi(slot, out state);

        public BotRandomizerWriteLeaseResult AcquireWriteLease(
            string owner,
            BotRandomizerCosmeticWriteClaim[] claims)
            => _plugin.AcquireWriteLeaseForApi(owner, claims);

        public BotRandomizerWriteLeaseResult ReplaceWriteLease(
            string leaseToken,
            BotRandomizerCosmeticWriteClaim[] claims)
            => _plugin.ReplaceWriteLeaseForApi(leaseToken, claims);

        public bool HeartbeatWriteLease(string leaseToken)
            => _plugin._writeLeases.Heartbeat(leaseToken);

        public bool ReleaseWriteLease(string leaseToken)
            => _plugin.ReleaseWriteLeaseForApi(leaseToken);

        public int ReleaseWriteLeasesByOwner(string owner)
            => _plugin.ReleaseWriteLeasesByOwnerForApi(owner);

        public BotRandomizerDiagnostics GetDiagnostics()
            => _plugin.GetDiagnosticsForApi();
    }

    private BotRandomizerProviderInfo GetProviderInfoForApi()
        => new()
        {
            ApiVersion = BotRandomizerContract.ApiVersion,
            ProviderEpoch = _providerEpoch,
            MapEpoch = _mapEpoch,
            Ready = !_draining && _catalog is not null && _roller is not null,
            Draining = _draining,
            EconAttributeWriterAvailable = _applicator?.NativeAvailable == true,
            WeaponPrebuildAvailable = _weaponItemViews?.NativeAvailable == true,
            CatalogRepository = _catalog?.SourceRepository ?? string.Empty,
            CatalogCommit = _catalog?.SourceCommit ?? string.Empty,
            LeaseTimeoutMilliseconds = BotRandomizerContract.LeaseTimeoutMilliseconds
        };

    private bool TryGetManagedBotForApi(int slot, out BotRandomizerManagedBot result)
    {
        result = new BotRandomizerManagedBot { Slot = slot };
        if (_draining)
            return false;

        var player = Utilities.GetPlayerFromSlot(slot);
        var state = GetOrCreateState(player);
        if (player is not { IsValid: true, IsBot: true, IsHLTV: false }
            || state is null)
        {
            return false;
        }

        var hasLease = _writeLeases.TryGetPolicy(
            slot,
            state.Incarnation,
            out _,
            out var owner);
        var pawn = player.PlayerPawn?.Value;
        result = new BotRandomizerManagedBot
        {
            Slot = slot,
            UserId = state.UserId,
            Incarnation = state.Incarnation,
            SteamId = player.SteamID,
            PawnEntityIndex = pawn is { IsValid: true } ? (int)pawn.Index : -1,
            Team = state.Loadout.Team,
            HasWriteLease = hasLease,
            LeaseOwner = owner
        };
        return true;
    }

    private BotRandomizerWriteLeaseResult AcquireWriteLeaseForApi(
        string owner,
        BotRandomizerCosmeticWriteClaim[] claims)
    {
        if (_draining)
            return FailWriteLease("provider_draining");
        SweepExpiredWriteLeases();
        if (!TryNormalizeWriteClaims(claims, out var normalized, out var reason))
        {
            _writeLeases.RecordRejectedRequest();
            return FailWriteLease(reason);
        }
        if (!_writeLeases.TryAcquire(owner ?? string.Empty, normalized, out var lease, out reason))
            return FailWriteLease(reason);

        RefreshLeasePolicySlots(lease.Claims.Keys);
        return SuccessWriteLease(lease);
    }

    private BotRandomizerWriteLeaseResult ReplaceWriteLeaseForApi(
        string leaseToken,
        BotRandomizerCosmeticWriteClaim[] claims)
    {
        if (_draining)
            return FailWriteLease("provider_draining");
        SweepExpiredWriteLeases();
        if (!TryNormalizeWriteClaims(claims, out var normalized, out var reason))
        {
            _writeLeases.RecordRejectedRequest();
            return FailWriteLease(reason);
        }
        if (!_writeLeases.TryReplace(
                leaseToken ?? string.Empty,
                normalized,
                out var lease,
                out var affectedSlots,
                out reason))
        {
            return FailWriteLease(reason);
        }

        RefreshLeasePolicySlots(affectedSlots);
        return SuccessWriteLease(lease);
    }

    private bool ReleaseWriteLeaseForApi(string leaseToken)
    {
        if (!_writeLeases.TryRelease(leaseToken ?? string.Empty, out var affectedSlots))
            return false;
        RefreshLeasePolicySlots(affectedSlots);
        return true;
    }

    private int ReleaseWriteLeasesByOwnerForApi(string owner)
    {
        var released = _writeLeases.ReleaseOwner(owner ?? string.Empty, out var affectedSlots);
        if (released > 0)
            RefreshLeasePolicySlots(affectedSlots);
        return released;
    }

    private BotRandomizerDiagnostics GetDiagnosticsForApi()
    {
        var counters = _writeLeases.GetCounters();
        return new BotRandomizerDiagnostics
        {
            Ready = !_draining && _catalog is not null && _roller is not null,
            ActiveLeases = counters.ActiveLeases,
            LeasedSlots = counters.LeasedSlots,
            AcquiredLeases = counters.AcquiredLeases,
            ReplacedLeases = counters.ReplacedLeases,
            ReleasedLeases = counters.ReleasedLeases,
            RevokedLeases = counters.RevokedLeases,
            ExpiredLeases = counters.ExpiredLeases,
            RejectedRequests = counters.RejectedRequests
        };
    }

    private bool TryNormalizeWriteClaims(
        BotRandomizerCosmeticWriteClaim[]? requestedClaims,
        out IReadOnlyDictionary<int, LeasedCosmeticWriteClaim> normalized,
        out string reason)
    {
        normalized = new Dictionary<int, LeasedCosmeticWriteClaim>();
        reason = string.Empty;
        if (_catalog is null || _roller is null)
        {
            reason = "provider_not_ready";
            return false;
        }
        if (requestedClaims is null || requestedClaims.Length == 0)
        {
            reason = "no_positive_claims";
            return false;
        }
        if (requestedClaims.Length > 64)
        {
            reason = "too_many_slots";
            return false;
        }

        var claimsBySlot = new Dictionary<int, LeasedCosmeticWriteClaim>();
        foreach (var requested in requestedClaims)
        {
            if (requested is null)
            {
                reason = "null_claim";
                return false;
            }
            if (claimsBySlot.ContainsKey(requested.Slot))
            {
                reason = $"duplicate_slot:{requested.Slot}";
                return false;
            }

            var player = Utilities.GetPlayerFromSlot(requested.Slot);
            var state = GetOrCreateState(player);
            if (player is not { IsValid: true, IsBot: true, IsHLTV: false }
                || state is null)
            {
                reason = $"slot_not_managed:{requested.Slot}";
                return false;
            }
            if (state.Incarnation != requested.Incarnation)
            {
                reason = $"stale_incarnation:{requested.Slot}";
                return false;
            }

            var weaponPolicies = new Dictionary<ushort, WeaponWritePolicy>();
            foreach (var weapon in requested.Weapons ?? [])
            {
                if (weapon is null)
                {
                    reason = $"null_weapon_claim:{requested.Slot}";
                    return false;
                }
                if (weapon.WeaponDefinitionIndex is <= 0 or > ushort.MaxValue
                    || !_catalog.TryGetWeapon((ushort)weapon.WeaponDefinitionIndex, out var catalogWeapon))
                {
                    reason = $"unknown_weapon:{requested.Slot}:{weapon.WeaponDefinitionIndex}";
                    return false;
                }
                if (weapon.PaintUsesLegacyModel is not null && !weapon.Paint)
                {
                    reason = $"legacy_hint_without_paint:{requested.Slot}:{weapon.WeaponDefinitionIndex}";
                    return false;
                }

                var policy = new WeaponWritePolicy(
                    weapon.Paint,
                    weapon.Stickers,
                    weapon.Keychain,
                    weapon.PaintUsesLegacyModel);
                if (!policy.ClaimsAnything)
                    continue;
                if (!weaponPolicies.TryAdd(catalogWeapon.DefIndex, policy))
                {
                    reason = $"duplicate_weapon:{requested.Slot}:{weapon.WeaponDefinitionIndex}";
                    return false;
                }
            }

            var writePolicy = new CosmeticWritePolicy(
                requested.Agent,
                requested.Knife,
                requested.Gloves,
                requested.MusicKit,
                weaponPolicies);
            if (!writePolicy.ClaimsAnything)
            {
                reason = $"no_positive_claims:{requested.Slot}";
                return false;
            }

            claimsBySlot.Add(
                requested.Slot,
                new LeasedCosmeticWriteClaim(
                    requested.Incarnation,
                    requested.SubjectSteamId,
                    writePolicy));
        }

        normalized = claimsBySlot;
        return true;
    }

    private void SweepExpiredWriteLeases()
    {
        var affectedSlots = _writeLeases.SweepExpired();
        if (affectedSlots.Length > 0)
            RefreshLeasePolicySlots(affectedSlots);
    }

    private void RefreshLeasePolicySlots(IEnumerable<int> slots)
    {
        foreach (var slot in slots.Distinct())
        {
            _states.BumpGeneration(slot);
            _applicator?.ClearSlot(slot);
            RestoreBot(slot, CosmeticScope.All);
        }
    }

    private BotRandomizerWriteLeaseResult SuccessWriteLease(CosmeticWriteLease lease)
        => new()
        {
            Ok = true,
            LeaseToken = lease.Token,
            ProviderEpoch = _providerEpoch,
            Reason = "ok",
            Slots = lease.Claims.Keys.Order().ToArray()
        };

    private BotRandomizerWriteLeaseResult FailWriteLease(string reason)
        => new()
        {
            Ok = false,
            ProviderEpoch = _providerEpoch,
            Reason = reason
        };
}
