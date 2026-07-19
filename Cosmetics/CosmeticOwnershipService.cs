using BotRandomizer.API;

namespace BotRandomizer;

internal enum OwnershipChangeKind
{
    Acquired,
    Released,
    Expired,
    Preempted
}

internal sealed record OwnershipChange(
    int PlayerSlot,
    CosmeticScope Scope,
    OwnershipChangeKind Kind,
    CosmeticReleaseMode ReleaseMode);

internal sealed class CosmeticOwnershipService : IBotCosmeticOwnershipApi, IDisposable
{
    private const int MinimumTtlSeconds = 1;
    private const int MaximumTtlSeconds = 60;
    private const int MaximumPlayerSlot = 63;
    private const int MaximumOwnerLength = 64;

    private readonly object _sync = new();
    private readonly Dictionary<long, Lease> _leases = new();
    private readonly Func<DateTimeOffset> _clock;
    private long _nextLeaseId = 1;
    private bool _available = true;

    internal CosmeticOwnershipService(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal event Action<OwnershipChange>? Changed;

    public int ApiVersion => BotRandomizerApiContract.Version;
    public bool IsAvailable
    {
        get
        {
            lock (_sync)
                return _available;
        }
    }

    public CosmeticLeaseResult AcquireLease(
        string owner,
        int playerSlot,
        CosmeticScope scope,
        CosmeticLeasePurpose purpose = CosmeticLeasePurpose.Replay,
        int ttlSeconds = 10)
    {
        var validationError = ValidateRequest(owner, playerSlot, scope, purpose);
        if (validationError is not null)
            return new(false, 0, DateTimeOffset.MinValue, validationError);

        List<OwnershipChange> changes;
        CosmeticLeaseResult result;
        lock (_sync)
        {
            if (!_available)
                return new(false, 0, DateTimeOffset.MinValue, "provider_unavailable");

            var now = _clock();
            changes = RemoveExpiredLocked(now);
            var priority = GetPriority(purpose);
            var conflicts = _leases.Values
                .Where(lease => lease.PlayerSlot == playerSlot
                    && lease.Owner != owner
                    && (lease.Scope & scope) != CosmeticScope.None)
                .ToArray();

            if (conflicts.Any(lease => lease.Priority >= priority))
            {
                result = new(false, 0, DateTimeOffset.MinValue, "scope_owned_by_equal_or_higher_priority_provider");
            }
            else
            {
                foreach (var conflict in conflicts)
                {
                    _leases.Remove(conflict.LeaseId);
                    changes.Add(new OwnershipChange(
                        conflict.PlayerSlot,
                        conflict.Scope,
                        OwnershipChangeKind.Preempted,
                        CosmeticReleaseMode.LeaveCurrent));
                }

                var leaseId = _nextLeaseId++;
                var expiresAt = now.AddSeconds(ClampTtl(ttlSeconds));
                _leases.Add(leaseId, new Lease(
                    owner,
                    playerSlot,
                    scope,
                    purpose,
                    priority,
                    leaseId,
                    expiresAt));
                changes.Add(new OwnershipChange(
                    playerSlot,
                    scope,
                    OwnershipChangeKind.Acquired,
                    CosmeticReleaseMode.LeaveCurrent));
                result = new(true, leaseId, expiresAt, "acquired");
            }
        }

        Notify(changes);
        return result;
    }

    public bool RenewLease(string owner, int playerSlot, long leaseId, int ttlSeconds = 10)
    {
        List<OwnershipChange> changes;
        bool renewed;
        lock (_sync)
        {
            if (!_available)
                return false;

            var now = _clock();
            changes = RemoveExpiredLocked(now);
            if (_leases.TryGetValue(leaseId, out var lease)
                && lease.Owner == owner
                && lease.PlayerSlot == playerSlot)
            {
                _leases[leaseId] = lease with { ExpiresAt = now.AddSeconds(ClampTtl(ttlSeconds)) };
                renewed = true;
            }
            else
            {
                renewed = false;
            }
        }

        Notify(changes);
        return renewed;
    }

    public bool ReleaseLease(
        string owner,
        int playerSlot,
        long leaseId,
        CosmeticReleaseMode mode = CosmeticReleaseMode.RestoreBaseline)
    {
        if (!Enum.IsDefined(typeof(CosmeticReleaseMode), mode))
            return false;

        OwnershipChange? change = null;
        lock (_sync)
        {
            if (!_available
                || !_leases.TryGetValue(leaseId, out var lease)
                || lease.Owner != owner
                || lease.PlayerSlot != playerSlot)
            {
                return false;
            }

            _leases.Remove(leaseId);
            change = new OwnershipChange(playerSlot, lease.Scope, OwnershipChangeKind.Released, mode);
        }

        Changed?.Invoke(change);
        return true;
    }

    public IReadOnlyList<CosmeticOwnershipStatus> GetStatus(int playerSlot)
    {
        lock (_sync)
        {
            var now = _clock();
            return _leases.Values
                .Where(lease => lease.PlayerSlot == playerSlot && lease.ExpiresAt > now)
                .OrderByDescending(lease => lease.Priority)
                .Select(ToStatus)
                .ToArray();
        }
    }

    public IReadOnlyList<CosmeticOwnershipStatus> GetAllStatuses()
    {
        lock (_sync)
        {
            var now = _clock();
            return _leases.Values
                .Where(lease => lease.ExpiresAt > now)
                .OrderBy(lease => lease.PlayerSlot)
                .ThenByDescending(lease => lease.Priority)
                .Select(ToStatus)
                .ToArray();
        }
    }

    internal bool CanWrite(int playerSlot, CosmeticScope scope)
    {
        lock (_sync)
        {
            if (!_available)
                return false;

            var now = _clock();
            return !_leases.Values.Any(lease => lease.PlayerSlot == playerSlot
                && lease.ExpiresAt > now
                && (lease.Scope & scope) != CosmeticScope.None);
        }
    }

    internal void CleanupExpired()
    {
        List<OwnershipChange> changes;
        lock (_sync)
            changes = RemoveExpiredLocked(_clock());
        Notify(changes);
    }

    internal void ReleaseSlot(int playerSlot)
    {
        lock (_sync)
        {
            foreach (var leaseId in _leases.Values
                .Where(lease => lease.PlayerSlot == playerSlot)
                .Select(lease => lease.LeaseId)
                .ToArray())
            {
                _leases.Remove(leaseId);
            }
        }
    }

    internal void Reset()
    {
        lock (_sync)
            _leases.Clear();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _available = false;
            _leases.Clear();
        }
    }

    private List<OwnershipChange> RemoveExpiredLocked(DateTimeOffset now)
    {
        var expired = _leases.Values.Where(lease => lease.ExpiresAt <= now).ToArray();
        var changes = new List<OwnershipChange>(expired.Length);
        foreach (var lease in expired)
        {
            _leases.Remove(lease.LeaseId);
            changes.Add(new OwnershipChange(
                lease.PlayerSlot,
                lease.Scope,
                OwnershipChangeKind.Expired,
                CosmeticReleaseMode.RestoreBaseline));
        }
        return changes;
    }

    private static string? ValidateRequest(
        string owner,
        int playerSlot,
        CosmeticScope scope,
        CosmeticLeasePurpose purpose)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > MaximumOwnerLength)
            return "invalid_owner";
        if (playerSlot is < 0 or > MaximumPlayerSlot)
            return "invalid_player_slot";
        if (scope == CosmeticScope.None || (scope & ~CosmeticScope.All) != CosmeticScope.None)
            return "invalid_scope";
        if (!Enum.IsDefined(typeof(CosmeticLeasePurpose), purpose))
            return "invalid_purpose";
        return null;
    }

    private static int GetPriority(CosmeticLeasePurpose purpose)
        => purpose switch
        {
            CosmeticLeasePurpose.DefaultProvider => 0,
            CosmeticLeasePurpose.AdminOverride => 50,
            CosmeticLeasePurpose.Replay => 100,
            CosmeticLeasePurpose.EmergencyRestore => 1000,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };

    private static int ClampTtl(int ttlSeconds)
        => Math.Clamp(ttlSeconds, MinimumTtlSeconds, MaximumTtlSeconds);

    private static CosmeticOwnershipStatus ToStatus(Lease lease)
        => new(
            lease.Owner,
            lease.PlayerSlot,
            lease.Scope,
            lease.Purpose,
            lease.LeaseId,
            lease.ExpiresAt);

    private void Notify(IEnumerable<OwnershipChange> changes)
    {
        foreach (var change in changes)
            Changed?.Invoke(change);
    }

    private sealed record Lease(
        string Owner,
        int PlayerSlot,
        CosmeticScope Scope,
        CosmeticLeasePurpose Purpose,
        int Priority,
        long LeaseId,
        DateTimeOffset ExpiresAt);
}
