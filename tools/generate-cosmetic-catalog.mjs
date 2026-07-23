import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const [
  itemsSourceArgument,
  englishSourceArgument,
  proLoadoutSourceArgument,
  outputArgument,
  commit
] = process.argv.slice(2);
if (
  itemsSourceArgument === undefined ||
  englishSourceArgument === undefined ||
  proLoadoutSourceArgument === undefined ||
  outputArgument === undefined ||
  commit === undefined
) {
  throw new Error(
    "Usage: node tools/generate-cosmetic-catalog.mjs " +
      "<cs2-lib/src/items.ts> <cs2-lib/scripts/data/english.json> " +
      "<offline-demo-evidence.json> " +
      "<output.json> <40-char-commit>"
  );
}
if (!/^[0-9a-f]{40}$/i.test(commit)) {
  throw new Error("The cs2-lib commit must be a full 40-character SHA.");
}

function parseGeneratedValue(source, opening) {
  const start = source.indexOf(`= ${opening}`);
  const closing = opening === "[" ? "]" : "}";
  const end = source.lastIndexOf(closing);
  if (start === -1 || end === -1 || end <= start) {
    throw new Error(`Unable to find generated ${opening}${closing} value.`);
  }
  return JSON.parse(source.slice(start + 2, end + 1));
}

const itemsSource = await readFile(resolve(itemsSourceArgument), "utf8");
const items = parseGeneratedValue(itemsSource, "[");
const english = JSON.parse(await readFile(resolve(englishSourceArgument), "utf8"));
const proLoadouts = JSON.parse(
  await readFile(resolve(proLoadoutSourceArgument), "utf8")
);
if (
  proLoadouts.version !== 1 ||
  proLoadouts.source?.format !== "cs2-demotracer-manifest-v8" ||
  proLoadouts.source?.weighting !== "distinct-owners" ||
  !/^[0-9a-f]{64}$/i.test(proLoadouts.source?.converterSha256 ?? "") ||
  !/^[0-9a-f]{64}$/i.test(proLoadouts.source?.corpusDigest ?? "")
) {
  throw new Error("The offline demo evidence has invalid source metadata.");
}

const rarityByColor = new Map([
  ["#b0c3d9", "consumer"],
  ["#5e98d9", "industrial"],
  ["#4b69ff", "milSpec"],
  ["#8847ff", "restricted"],
  ["#d32ce6", "classified"],
  ["#eb4b4b", "covert"],
  ["#e4ae39", "contraband"]
]);

function requireRarity(item) {
  const rarity = rarityByColor.get(item.rarity);
  if (rarity === undefined) {
    throw new Error(`Unsupported rarity ${item.rarity} for item ${item.id}.`);
  }
  return rarity;
}

function requireTranslation(item, translations) {
  const translation = translations[item.id];
  if (translation?.name === undefined) {
    throw new Error(`Missing translation for item ${item.id}.`);
  }
  return translation;
}

function suffixAfterOwner(name) {
  const parts = name.split(" | ");
  return parts.length > 1 ? parts.slice(1).join(" | ") : name;
}

function weaponPaint(item) {
  return {
    paintKit: item.index,
    rarity: requireRarity(item),
    legacy: item.legacy === true,
    wearMin: item.wearMin ?? 0,
    wearMax: item.wearMax ?? 1
  };
}

function knifePaint(item) {
  return {
    paintKit: item.index,
    finish: suffixAfterOwner(requireTranslation(item, english).name),
    wearMin: item.wearMin ?? 0,
    wearMax: item.wearMax ?? 1
  };
}

function groupPaints(type, project) {
  const groups = new Map();
  for (const item of items) {
    if (item.type !== type || item.base === true || !(item.def > 0) || !(item.index > 0)) {
      continue;
    }
    const paints = groups.get(item.def) ?? [];
    paints.push(project(item));
    groups.set(item.def, paints);
  }
  return groups;
}

const baseWeapons = new Map(
  items
    .filter((item) => item.type === "weapon" && item.base === true)
    .map((item) => [item.def, item])
);
const weaponGroups = groupPaints("weapon", weaponPaint);
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

const knives = [...groupPaints("melee", knifePaint)]
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

function stickerFinish(name) {
  const match = name.match(/\((Lenticular|Gold|Holo|Glitter|Foil)\)(?:\s*\||$)/i);
  return match?.[1]?.toLowerCase() ?? "paper";
}

const stickerItems = items.filter(
  (item) => item.type === "sticker" && item.base !== true && item.index > 0
);
const stickerItemCategories = stickerItems.map(
  (item) => requireTranslation(item, english).category
);
if (stickerItemCategories.some((category) => category === undefined)) {
  throw new Error("A sticker is missing its category.");
}
const stickerCategories = [...new Set(stickerItemCategories)].sort((left, right) =>
  left.localeCompare(right, "en")
);
const stickerCategoryIndexes = new Map(
  stickerCategories.map((category, index) => [category, index])
);
const stickerKits = stickerItems
  .map((item) => {
    const translation = requireTranslation(item, english);
    const category = stickerCategoryIndexes.get(translation.category);
    if (category === undefined) {
      throw new Error(`Missing category index for sticker ${item.id}.`);
    }
    return {
      defIndex: item.index,
      finish: stickerFinish(translation.name),
      category
    };
  })
  .sort((a, b) => a.defIndex - b.defIndex);

const knifeFinishBySpec = new Map();
for (const knife of knives) {
  for (const paint of knife.paints) {
    knifeFinishBySpec.set(`${knife.defIndex}:${paint.paintKit}`, paint.finish);
  }
}

const knifeFinishObservations = new Map();
const unmatchedKnifeSpecs = new Set();
let knifeObservations = 0;
let matchedKnifeObservations = 0;
for (const template of proLoadouts.knives ?? []) {
  if (!Number.isSafeInteger(template.owners) || template.owners <= 0) {
    throw new Error("The pro loadout catalog contains invalid knife owner evidence.");
  }
  knifeObservations += template.owners;
  const finish = knifeFinishBySpec.get(
    `${template.defIndex}:${template.paintKit}`
  );
  if (finish === undefined) {
    unmatchedKnifeSpecs.add(`${template.defIndex}:${template.paintKit}`);
    continue;
  }
  knifeFinishObservations.set(
    finish,
    (knifeFinishObservations.get(finish) ?? 0) + template.owners
  );
  matchedKnifeObservations += template.owners;
}
if (unmatchedKnifeSpecs.size > 0) {
  throw new Error(
    "The pro loadout catalog contains knife specs missing from cs2-lib: " +
      [...unmatchedKnifeSpecs].sort().slice(0, 20).join(", ")
  );
}
const maximumKnifeFinishObservations = Math.max(...knifeFinishObservations.values());
if (!Number.isFinite(maximumKnifeFinishObservations) || matchedKnifeObservations <= 0) {
  throw new Error("The pro-demo report has no matchable knife finish observations.");
}
const knifeFinishPreferences = [...knifeFinishObservations]
  .map(([finish, observations]) => ({
    finish,
    observations,
    weight: Math.max(
      1,
      Math.min(
        10,
        Math.round(1 + 9 * Math.sqrt(observations / maximumKnifeFinishObservations))
      )
    )
  }))
  .sort((left, right) => left.finish.localeCompare(right.finish, "en"));

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
    commit: commit.toLowerCase(),
    proDemo: {
      logicalMaps: proLoadouts.source.parsedDemoFiles,
      knifeObservations,
      matchedKnifeObservations,
      converterSha256: proLoadouts.source.converterSha256.toLowerCase(),
      corpusDigest: proLoadouts.source.corpusDigest.toLowerCase()
    }
  },
  weapons,
  knives,
  knifeFinishPreferences,
  gloves,
  stickerCategories,
  stickerKits,
  keychainDefinitions: uniqueIndexes("keychain"),
  musicKits: uniqueIndexes("musickit")
};

if (!catalog.keychainDefinitions.includes(37)) {
  throw new Error("Sticker Slab keychain definition 37 is missing.");
}

await writeFile(resolve(outputArgument), `${JSON.stringify(catalog, null, 2)}\n`, "utf8");
console.log(
  `wrote ${outputArgument}: ${weapons.length} weapons, ` +
    `${stickerKits.length} stickers in ${stickerCategories.length} categories, ` +
    `${knifeFinishPreferences.length} observed knife finishes`
);
