namespace BotRandomizer;

internal sealed class CosmeticStateStore
{
    private readonly Dictionary<int, SlotCosmeticState> _states = new();
    private long _nextGeneration = 1;

    internal IReadOnlyCollection<SlotCosmeticState> States => _states.Values;

    internal SlotCosmeticState GetOrCreate(
        int slot,
        int userId,
        byte team,
        Func<int?, BotCosmeticLoadout> loadoutFactory)
    {
        if (_states.TryGetValue(slot, out var state) && state.UserId == userId)
        {
            if (state.Loadout.Team == team)
                return state;

            var replacement = loadoutFactory(state.Loadout.MusicKit);
            PreserveStableWearables(state.Loadout, replacement);
            state.Loadout = replacement;
            state.Generation = NextGeneration();
            return state;
        }

        state = new SlotCosmeticState(
            slot,
            userId,
            NextGeneration(),
            loadoutFactory(null));
        _states[slot] = state;
        return state;
    }

    internal SlotCosmeticState? Reroll(
        int slot,
        int userId,
        byte team,
        bool preserveMusic,
        Func<int?, BotCosmeticLoadout> loadoutFactory)
    {
        var hasExisting = _states.TryGetValue(slot, out var existing) && existing.UserId == userId;
        var musicKit = hasExisting && preserveMusic ? existing!.Loadout.MusicKit : (int?)null;
        var replacement = loadoutFactory(musicKit);
        if (hasExisting)
            PreserveStableWearables(existing!.Loadout, replacement);

        var state = new SlotCosmeticState(
            slot,
            userId,
            NextGeneration(),
            replacement);
        _states[slot] = state;
        return state;
    }

    internal void InvalidateWeaponSelections()
    {
        foreach (var state in _states.Values)
        {
            state.Loadout.Weapons.Clear();
            state.Generation = NextGeneration();
        }
    }

    internal bool TryGet(int slot, out SlotCosmeticState state)
        => _states.TryGetValue(slot, out state!);

    internal bool IsCurrent(int slot, int userId, long generation)
        => _states.TryGetValue(slot, out var state)
            && state.UserId == userId
            && state.Generation == generation;

    internal long BumpGeneration(int slot)
    {
        if (!_states.TryGetValue(slot, out var state))
            return 0;

        state.Generation = NextGeneration();
        return state.Generation;
    }

    internal void Remove(int slot)
    {
        _states.Remove(slot);
        NextGeneration();
    }

    internal void Reset()
    {
        _states.Clear();
        NextGeneration();
    }

    private long NextGeneration() => _nextGeneration++;

    private static void PreserveStableWearables(
        BotCosmeticLoadout existing,
        BotCosmeticLoadout replacement)
    {
        replacement.Knife = existing.Knife;
        replacement.Glove = existing.Glove;
    }
}

internal sealed class SlotCosmeticState(
    int slot,
    int userId,
    long generation,
    BotCosmeticLoadout loadout)
{
    internal int Slot { get; } = slot;
    internal int UserId { get; } = userId;
    internal long Generation { get; set; } = generation;
    internal BotCosmeticLoadout Loadout { get; set; } = loadout;
}
