# CS2-Bot-Randomizer

CounterStrikeSharp plugin that gives each bot a stable cosmetic loadout: agent,
music kit, knife, gloves, weapon paint, up to five stickers, and an optional
charm.

## What changed in 1.4

- Weapon paints, knife paints, gloves, stickers, charms, model-specific sticker
  schemas, and wear ranges come from the bundled `cosmetic_catalog.json`.
- Charm offsets come from the bundled `charm_placements.json`. Its positions
  were observed in parsed CS2 demos and are grouped by the weapon definition;
  positions are never shared between different weapon models.
- The current placement pool contains 158 distinct positions for 16 weapons,
  built from 20 parsed demos (19 contained charm observations), including 12
  current-version FACEIT demos.
- The catalog is generated from [`ianlucas/cs2-lib`](https://github.com/ianlucas/cs2-lib),
  records the exact source commit, is validated at plugin startup, and requires
  no runtime network access.
- Sticker and charm integer attributes are bit-reinterpreted as floats, as
  required by CS2's `stored_as_integer` economic attributes.
- Sticker combinations reserve distinct weapon wear values to avoid the CS2
  client material cache displaying another bot's stickers.
- A bot owns one complete loadout. Weapon paint, sticker, and charm attributes
  are supplied in a preconstructed `CEconItemView` before `GiveNamedItem`
  creates the weapon. The plugin no longer clears or rebuilds economic
  attributes on live gun entities.
- BotBuy replacement guns use the same engine construction hook as normal
  purchases, so the final M4A4, M4A1-S, MP5-SD, and PP-Bizon no longer depend
  on guessed post-purchase retry delays.
- Knife and glove writes are fingerprinted by bot, pawn, entity, and cosmetic
  selection. Spawn retries become no-ops after the intended economic state has
  already been installed.
- Per-slot callbacks capture both the user ID and loadout generation. Stale
  callbacks cannot write after a team change, reroll, disconnect, slot reuse,
  or external ownership handoff.
- `BotRandomizer.API` exposes expiring per-slot, per-scope leases for replay or
  override plugins. Releasing a lease restores the frozen random baseline by
  default.

The plugin deliberately keeps the original, verified four-knife subclass set.
The larger catalog is used to select only paints valid for the selected weapon,
knife, or glove definition; it does not guess IDs from numeric ranges.

## Runtime behavior

- Each `(bot slot, weapon definition)` gets one stable weapon selection until a
  team change, map change, or explicit reroll.
- Weapon entities are born with their complete cosmetic state through the
  `GiveNamedItem` pre-hook. Only the constructed item view's
  `NetworkedDynamicAttributes` list is populated; no live-weapon attribute list
  is cleared or rewritten afterward.
- A weapon receives `0..5` stickers. Sticker slots are contiguous and each
  schema index is constrained to the selected paint's actual HD/legacy model.
- A weapon has a 70% chance to receive one charm in keychain slot `0`.
- For a weapon covered by `charm_placements.json`, the charm receives one
  uniformly selected, demo-observed position for that exact weapon definition.
  Weapons without observations omit custom offsets and retain CS2's own
  weapon-aware default attachment position.
- Charm seeds stay in CS2's valid `1..100000` range.
- Sticker Slab (keychain definition `37`) also receives a real sticker kit ID.
- Weapon setting changes and rerolls affect the next weapon constructed for
  that bot. They deliberately do not rewrite a gun that is already live.

## Commands

```text
br_status
br_set <enabled|weapons|knives|gloves|agents|music|stickers|charms> <on|off>
br_reroll [all|slot]
br_ownership
```

Changing settings, rerolling, and viewing ownership require `@css/cvar`.
`br_status` is read-only.

## Optional ownership API

The capability name is:

```text
botrandomizer:cosmetic_ownership:v1
```

Consumers compile against `BotRandomizer.API.dll` and acquire a short-lived
lease for the exact bot slot and scopes they will write. Active replay code
must renew the lease; expired leases are reclaimed automatically. Consumers
should release with `RestoreBaseline` during normal stop/handoff and unload.

For CounterStrikeSharp shared-type identity, install the contract assembly at:

```text
addons/counterstrikesharp/shared/BotRandomizer.API/BotRandomizer.API.dll
```

Do not ship private, differing copies of the contract assembly in multiple
plugin directories.

## Build and validate

```powershell
C:\Users\Uni\.dotnet\dotnet.exe build -c Release
C:\Users\Uni\.dotnet\dotnet.exe run `
  --project tests\BotRandomizer.SelfTest\BotRandomizer.SelfTest.csproj `
  -c Release -- cosmetic_catalog.json
```

The self-test validates catalog counts and provenance, exact designer-name to
definition-index mappings (including BotBuy's CT replacement guns), integer
attribute bit encoding, process-unique custom item IDs, sticker schema bounds,
Sticker Slab payloads, keychain seed bounds, demo-observed weapon-specific
charm placement, 70% charm probability, wear-cache isolation, and ownership
lease expiry.

## Refresh the catalog

Clone a reviewed `ianlucas/cs2-lib` revision, then run:

```powershell
node tools\generate-cosmetic-catalog.mjs `
  C:\path\to\cs2-lib\src\items.ts `
  cosmetic_catalog.json `
  <full-40-character-cs2-lib-commit>
```

Review the generated diff and run the self-test before publishing. The plugin
never downloads or mutates the catalog at runtime.

To rebuild weapon-coupled charm positions from one or more reviewed
`cs2-demotracer` evidence reports:

```powershell
.\tools\generate-charm-placement-catalog.ps1 `
  -EvidencePath C:\path\to\evidence-a.json,C:\path\to\evidence-b.json `
  -OutputPath .\charm_placements.json
```

The generator deduplicates inventory observations before grouping exact
float32 positions by weapon definition. It never transfers a position between
different weapon models.

## Installation

1. Build or download the release.
2. Place `BotRandomizer.dll`, `cosmetic_catalog.json`, and
   `charm_placements.json` under
   `addons/counterstrikesharp/plugins/BotRandomizer/`.
3. Place `BotRandomizer.API.dll` in the shared path shown above.
4. Set `FollowCS2ServerGuidelines` to `false` in CounterStrikeSharp's
   `configs/core.json`.
5. Restart the server and check `br_status` before enabling another cosmetic
   writer.

## Credits and licensing

- Original plugin: [ed0ard/CS2-Bot-Randomizer](https://github.com/ed0ard/CS2-Bot-Randomizer)
- Catalog and inventory model: [ianlucas/cs2-lib](https://github.com/ianlucas/cs2-lib)
- Attribute encoding and cache workaround:
  [ianlucas/cs2-css-inventory-simulator](https://github.com/ianlucas/cs2-css-inventory-simulator)

See `THIRD_PARTY_NOTICES.md` for the MIT notice covering the adapted Ian Lucas
work. This repository remains licensed under AGPL-3.0.
