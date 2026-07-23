# CS2-Bot-Randomizer

CounterStrikeSharp plugin that gives each bot a stable cosmetic loadout: agent,
music kit, knife, gloves, weapon paint, up to five stickers, and an optional
charm.

## What changed in 1.4

- Weapon paints, knife paints, gloves, stickers, charms, model-specific sticker
  schemas, and wear ranges come from the bundled `cosmetic_catalog.json`.
- Professional demos are used offline to derive a small set of general
  preferences. Exact player inventories and weapon templates are not shipped or
  looked up at runtime.
- Weapon skins and stickers are always sampled from the complete current
  `cs2-lib` catalog. Cosmetics introduced after the demo corpus was recorded
  therefore remain reachable without a special fallback lane.
- The compact preferences cover weapon rarity, glove family, knife finish,
  sticker count, repeated-design probability, and sticker finish. They are
  ordinary integer weights in the randomizer rather than a large runtime table.
- Karambit, M9 Bayonet, Butterfly Knife, and Bayonet hold 70% of knife-type
  probability together. The remaining 30% favors other types seen on at least
  seven distinct owners in the reviewed demos, plus a 3% Classic Knife
  maintainer preference; one-off and near-one-off types are excluded.
- The observed sticker-count distribution is approximately 35% clean, 12%
  single, 8% pair, 8% triple, 25% four-sticker, and 12% five-sticker.
- Four-sticker crafts choose one finish theme for the whole craft. Repeated
  four-of-a-kind crafts favor Holo; non-repeated themed fours favor Gold, with
  Holo and Paper still common. This avoids independent per-slot "slot machine"
  results while continuing to use new sticker definitions.
- Charms deliberately remain independent and random.
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
  or map transition.

The runtime catalog records the parser hash and demo-corpus digest used to
derive its compact knife preferences.

## Runtime behavior

- Each `(bot slot, weapon definition)` gets one stable weapon selection until a
  team change, map change, or explicit reroll.
- Weapon entities are born with their complete cosmetic state through the
  `GiveNamedItem` pre-hook. Only the constructed item view's
  `NetworkedDynamicAttributes` list is populated; no live-weapon attribute list
  is cleared or rewritten afterward.
- Every weapon skin and sticker roll uses the current full catalog, with compact
  demo-derived weights applied to families and craft shapes.
- Knife type uses a ten-entry, 100-point weight list. Its selected finish is
  then weighted independently from demo evidence and resolved only against
  paints valid for that knife.
- Sticker crafts stay within one semantic category. Four-sticker crafts also
  stay within one finish theme, and may repeat one exact design according to
  the observed repeat rate.
- A small wear-value reservation preserves each selected craft while preventing
  the CS2 client material cache from displaying another bot's stickers.
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
```

Changing settings and rerolling require `@css/cvar`. `br_status` is read-only.

## Optional GUI profile

`randomizer_config.json` may be placed beside the plugin to configure compact
weights and cosmetic allowlists or blacklists. It is read once when the plugin
loads. Invalid profiles fall back to the built-in defaults; there is no
file-watcher or live entity rewrite.

## Build and validate

```powershell
C:\Users\Uni\.dotnet\dotnet.exe build -c Release
C:\Users\Uni\.dotnet\dotnet.exe run `
  --project tests\BotRandomizer.SelfTest\BotRandomizer.SelfTest.csproj `
  -c Release -- cosmetic_catalog.json
```

The self-test validates catalog counts and provenance, exact
designer-name to definition-index mappings (including BotBuy's CT replacement
guns), compact weighted distributions, integer attribute bit encoding, process-unique
custom item IDs, sticker schema bounds, Sticker Slab payloads, keychain seed
bounds, demo-observed weapon-specific charm placement, 70% charm probability,
wear-cache isolation, the 70/30 knife-type split, coherent Holo/Gold
four-sticker themes, and non-overlapping weapon sticker schemas.

## Rebuild professional demo evidence

Build `cs2-demotracer` in release mode, then point the resumable extractor at a
directory tree containing `.dem` files:

```powershell
node tools\build-pro-loadout-catalog.mjs `
  --demo-root C:\path\to\demos `
  --converter C:\path\to\cs2-demotracer.exe `
  --output .cache\pro-loadout-evidence.json `
  --report .cache\pro-loadout-report.json `
  --cache-dir .cache\pro-loadout-catalog `
  --workers 3
```

The extractor invokes the parser's opt-in cosmetic and sticker export with the
required acknowledgements, handles segmented `-pN.dem` files independently,
and caches one privacy-reduced evidence record per source file. Interrupted
runs resume from that cache; changed demos and a changed parser executable
invalidate the relevant records. The report may retain relative failure paths
for diagnostics. The evidence and report are offline analysis artifacts; the
plugin does not load or ship either file.

Add `--cached-only` to rebuild a catalog from the currently valid cache without
parsing any remaining demo files. This is useful for freezing a reviewed
partial corpus before later adding a small curated batch.

## Refresh the canonical cosmetic catalog

Clone a reviewed `ianlucas/cs2-lib` revision, then run:

```powershell
node tools\generate-cosmetic-catalog.mjs `
  C:\path\to\cs2-lib\src\items.ts `
  C:\path\to\cs2-lib\scripts\data\english.json `
  .cache\pro-loadout-evidence.json `
  cosmetic_catalog.json `
  <full-40-character-cs2-lib-commit>
```

The English metadata supplies stable sticker categories and finish names.
Observed knife finishes are joined directly by `(definition index, paint kit)`;
localized names are not used as keys. The generator reduces the offline evidence
to compact finish weights and provenance inside `cosmetic_catalog.json`. Review
the generated catalog and run the self-test before publishing. The plugin never
downloads or mutates it at runtime.

To rebuild weapon-coupled charm positions from one or more reviewed
`cs2-demotracer` evidence reports:

```powershell
.\tools\generate-charm-placement-catalog.ps1 `
  -EvidencePath C:\path\to\evidence-a.json,C:\path\to\evidence-b.json `
  -OutputPath .\charm_placements.json
```

The generator deduplicates inventory observations before grouping exact
float32 positions by weapon definition. It never transfers a position between
different weapon models. The published runtime file contains only weapon
definition keys and `[x, y, z]` arrays; parser details, demo hashes, and
observation counts remain in the input evidence reports.

## Installation

1. Build or download the release.
2. Place `BotRandomizer.dll`, `cosmetic_catalog.json`, and
   `charm_placements.json` under
   `addons/counterstrikesharp/plugins/BotRandomizer/`.
3. Set `FollowCS2ServerGuidelines` to `false` in CounterStrikeSharp's
   `configs/core.json`.
4. Restart the server and check `br_status` before enabling another cosmetic
   writer.

## Credits and licensing

- Original plugin: [ed0ard/CS2-Bot-Randomizer](https://github.com/ed0ard/CS2-Bot-Randomizer)
- Catalog and inventory model: [ianlucas/cs2-lib](https://github.com/ianlucas/cs2-lib)
- Attribute encoding and cache workaround:
  [ianlucas/cs2-css-inventory-simulator](https://github.com/ianlucas/cs2-css-inventory-simulator)

See `THIRD_PARTY_NOTICES.md` for the MIT notice covering the adapted Ian Lucas
work. This repository remains licensed under AGPL-3.0.
