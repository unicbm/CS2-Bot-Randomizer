import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const [itemsSourceArgument, outputArgument, commit] = process.argv.slice(2);
if (itemsSourceArgument === undefined || outputArgument === undefined || commit === undefined) {
  throw new Error(
    "Usage: node tools/generate-cosmetic-catalog.mjs <cs2-lib/src/items.ts> <output.json> <40-char-commit>"
  );
}
if (!/^[0-9a-f]{40}$/i.test(commit)) {
  throw new Error("The cs2-lib commit must be a full 40-character SHA.");
}

const source = await readFile(resolve(itemsSourceArgument), "utf8");
const arrayStart = source.indexOf("= [");
const arrayEnd = source.lastIndexOf("]");
if (arrayStart === -1 || arrayEnd === -1 || arrayEnd <= arrayStart) {
  throw new Error("Unable to find the generated CS2_ITEMS array.");
}

const items = JSON.parse(source.slice(arrayStart + 2, arrayEnd + 1));
const baseWeapons = new Map(
  items
    .filter((item) => item.type === "weapon" && item.base === true)
    .map((item) => [item.def, item])
);

function paint(item) {
  return {
    paintKit: item.index,
    legacy: item.legacy === true,
    wearMin: item.wearMin ?? 0,
    wearMax: item.wearMax ?? 1
  };
}

function groupPaints(type) {
  const groups = new Map();
  for (const item of items) {
    if (item.type !== type || item.base === true || !(item.def > 0) || !(item.index > 0)) {
      continue;
    }
    const paints = groups.get(item.def) ?? [];
    paints.push(paint(item));
    groups.set(item.def, paints);
  }
  return groups;
}

const weaponGroups = groupPaints("weapon");
const weapons = [...weaponGroups]
  .map(([defIndex, paints]) => {
    const base = baseWeapons.get(defIndex);
    if (base === undefined) {
      throw new Error(`Missing base weapon for definition ${defIndex}.`);
    }
    return {
      designerName: `weapon_${base.model}`,
      defIndex,
      stickerSchemaCount: base.stickerSchemaCount ?? 5,
      legacyStickerSchemaCount:
        base.legacyStickerSchemaCount ?? base.stickerSchemaCount ?? 5,
      paints: paints.sort((a, b) => a.paintKit - b.paintKit)
    };
  })
  .sort((a, b) => a.defIndex - b.defIndex);

const knives = [...groupPaints("melee")]
  .map(([defIndex, paints]) => ({
    defIndex,
    paints: paints.sort((a, b) => a.paintKit - b.paintKit)
  }))
  .sort((a, b) => a.defIndex - b.defIndex);

const gloves = items
  .filter(
    (item) =>
      item.type === "glove" &&
      item.base !== true &&
      item.def > 0 &&
      item.index > 0
  )
  .map((item) => ({
    defIndex: item.def,
    paintKit: item.index,
    wearMin: item.wearMin ?? 0,
    wearMax: item.wearMax ?? 1
  }))
  .sort((a, b) => a.defIndex - b.defIndex || a.paintKit - b.paintKit);

function uniqueIndexes(type) {
  return [
    ...new Set(
      items
        .filter((item) => item.type === type && item.base !== true && item.index > 0)
        .map((item) => item.index)
    )
  ].sort((a, b) => a - b);
}

const catalog = {
  source: {
    repository: "ianlucas/cs2-lib",
    commit: commit.toLowerCase()
  },
  weapons,
  knives,
  gloves,
  stickerKits: uniqueIndexes("sticker"),
  keychainDefinitions: uniqueIndexes("keychain"),
  musicKits: uniqueIndexes("musickit")
};

if (!catalog.keychainDefinitions.includes(37)) {
  throw new Error("Sticker Slab keychain definition 37 is missing.");
}

await writeFile(resolve(outputArgument), `${JSON.stringify(catalog, null, 2)}\n`, "utf8");
console.log(
  `wrote ${outputArgument}: ${weapons.length} weapons, ` +
    `${catalog.stickerKits.length} stickers, ${catalog.keychainDefinitions.length} keychains`
);
