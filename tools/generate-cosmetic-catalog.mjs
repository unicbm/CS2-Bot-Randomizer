import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const [
  itemsSourceArgument,
  englishSourceArgument,
  chineseSourceArgument,
  outputArgument,
  commit
] = process.argv.slice(2);

if (
  itemsSourceArgument === undefined ||
  englishSourceArgument === undefined ||
  chineseSourceArgument === undefined ||
  outputArgument === undefined ||
  commit === undefined
) {
  throw new Error(
    "Usage: node tools/generate-cosmetic-catalog.mjs " +
      "<cs2-lib/src/items.ts> <english.ts> <schinese.ts> <output.json> <40-char-commit>"
  );
}
if (!/^[0-9a-f]{40}$/i.test(commit)) {
  throw new Error("The cs2-lib commit must be a full 40-character SHA.");
}

function parseGeneratedArray(source, label) {
  const start = source.indexOf("= [");
  const end = source.lastIndexOf("]");
  if (start === -1 || end === -1 || end <= start) {
    throw new Error(`Unable to find the generated ${label} array.`);
  }
  return JSON.parse(source.slice(start + 2, end + 1));
}

function parseGeneratedObject(source, label) {
  const start = source.indexOf("= {");
  const end = source.lastIndexOf("}");
  if (start === -1 || end === -1 || end <= start) {
    throw new Error(`Unable to find the generated ${label} object.`);
  }
  return JSON.parse(source.slice(start + 2, end + 1));
}

const [itemsSource, englishSource, chineseSource] = await Promise.all([
  readFile(resolve(itemsSourceArgument), "utf8"),
  readFile(resolve(englishSourceArgument), "utf8"),
  readFile(resolve(chineseSourceArgument), "utf8")
]);
const items = parseGeneratedArray(itemsSource, "CS2_ITEMS");
const english = parseGeneratedObject(englishSource, "English translation");
const chinese = parseGeneratedObject(chineseSource, "Simplified Chinese translation");

const translate = (map, item, field, fallback = "") =>
  map[item.id]?.[field] ?? fallback;

function display(item) {
  const name = translate(english, item, "name", `Item ${item.id}`);
  return {
    id: item.id,
    name,
    nameZh: translate(chinese, item, "name", name),
    category: translate(english, item, "category") || undefined,
    categoryZh: translate(chinese, item, "category") || undefined,
    collection: translate(english, item, "collectionName") || undefined,
    collectionZh: translate(chinese, item, "collectionName") || undefined,
    image: item.image,
    rarity: item.rarity
  };
}

const baseWeapons = new Map(
  items
    .filter((item) => item.type === "weapon" && item.base === true)
    .map((item) => [item.def, item])
);
const baseKnives = new Map(
  items
    .filter(
      (item) =>
        item.type === "melee" &&
        item.base === true &&
        item.def >= 500 &&
        item.index === 0
    )
    .map((item) => [item.def, item])
);

function paint(item) {
  return {
    paintKit: item.index,
    legacy: item.legacy === true,
    wearMin: item.wearMin ?? 0,
    wearMax: item.wearMax ?? 1,
    ...display(item)
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
      ...display(base),
      designerName: `weapon_${base.model}`,
      defIndex,
      category: base.category,
      team: base.teams,
      stickerSchemaCount: base.stickerSchemaCount ?? 5,
      legacyStickerSchemaCount:
        base.legacyStickerSchemaCount ?? base.stickerSchemaCount ?? 5,
      paints: paints.sort((a, b) => a.paintKit - b.paintKit)
    };
  })
  .sort((a, b) => a.defIndex - b.defIndex);

const knives = [...groupPaints("melee")]
  .filter(([defIndex]) => baseKnives.has(defIndex))
  .map(([defIndex, paints]) => {
    const base = baseKnives.get(defIndex);
    return {
      designerName: `weapon_${base.model}`,
      defIndex,
      ...display(base),
      paints: paints.sort((a, b) => a.paintKit - b.paintKit)
    };
  })
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
    wearMax: item.wearMax ?? 1,
    ...display(item)
  }))
  .sort((a, b) => a.defIndex - b.defIndex || a.paintKit - b.paintKit);

function uniqueItems(type) {
  const byIndex = new Map();
  for (const item of items) {
    if (item.type === type && item.base !== true && item.index > 0 && !byIndex.has(item.index)) {
      byIndex.set(item.index, {
        index: item.index,
        ...display(item)
      });
    }
  }
  return [...byIndex.values()].sort((a, b) => a.index - b.index);
}

const stickerItems = uniqueItems("sticker");
const keychainItems = uniqueItems("keychain");
const musicKitItems = uniqueItems("musickit");
const agents = items
  .filter(
    (item) =>
      item.type === "agent" &&
      item.base !== true &&
      item.model &&
      (item.teams === 0 || item.teams === 1)
  )
  .map((item) => ({
    defIndex: item.def,
    team: item.teams === 0 ? 2 : 3,
    modelPath: `${item.model.replaceAll("/", "\\")}.vmdl`,
    ...display(item)
  }))
  .sort((a, b) => a.team - b.team || a.defIndex - b.defIndex);

const catalog = {
  schemaVersion: 2,
  source: {
    repository: "ianlucas/cs2-lib",
    commit: commit.toLowerCase(),
    assetsBaseUrl: "https://cdn.cstrike.app"
  },
  weapons,
  knives,
  gloves,
  stickerKits: stickerItems.map((item) => item.index),
  stickerItems,
  keychainDefinitions: keychainItems.map((item) => item.index),
  keychainItems,
  musicKits: musicKitItems.map((item) => item.index),
  musicKitItems,
  agents
};

if (!catalog.keychainDefinitions.includes(37)) {
  throw new Error("Sticker Slab keychain definition 37 is missing.");
}
if (catalog.knives.length !== 20) {
  throw new Error(`Expected 20 configurable knife types, found ${catalog.knives.length}.`);
}
if (catalog.agents.length === 0) {
  throw new Error("No agent cosmetics were generated.");
}

await writeFile(resolve(outputArgument), `${JSON.stringify(catalog, null, 2)}\n`, "utf8");
console.log(
  `wrote ${outputArgument}: ${weapons.length} weapons, ${catalog.knives.length} knives, ` +
    `${catalog.agents.length} agents, ${stickerItems.length} stickers, ` +
    `${keychainItems.length} keychains, ${musicKitItems.length} music kits`
);
