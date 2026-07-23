import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import {
  access,
  copyFile,
  link,
  mkdir,
  mkdtemp,
  readFile,
  readdir,
  rename,
  rm,
  stat,
  writeFile
} from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve, sep } from "node:path";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const CACHE_VERSION = 3;
const CATALOG_VERSION = 1;
const CONVERTER_TIMEOUT_MILLISECONDS = 5 * 60 * 1000;
const OWNER_HASH_DOMAIN = "cs2-bot-randomizer-pro-owner-v1\0";
const STEAM_ID64_ACCOUNT_BASE = 76_561_197_960_265_728n;

const options = parseArguments(process.argv.slice(2));
const demoRoot = resolve(options.demoRoot);
const converterPath = resolve(options.converter);
const outputPath = resolve(options.output);
const reportPath = resolve(
  options.report ?? `${outputPath.slice(0, -".json".length)}.report.json`
);
const cacheDirectory = resolve(options.cacheDir ?? ".cache/pro-loadout-catalog");
if (outputPath === reportPath) {
  throw new Error("--output and --report must resolve to different files.");
}

await access(demoRoot);
await access(converterPath);
await mkdir(cacheDirectory, { recursive: true });
await mkdir(dirname(outputPath), { recursive: true });
await mkdir(dirname(reportPath), { recursive: true });

const converterSha256 = await sha256File(converterPath);
const allDemoPaths = await findDemoFiles(demoRoot);
const matchingDemoPaths = options.match === undefined
  ? allDemoPaths
  : allDemoPaths.filter((path) =>
      new RegExp(options.match, "i").test(relative(demoRoot, path))
    );
const selectedDemoPaths = options.limit === undefined
  ? matchingDemoPaths
  : matchingDemoPaths.slice(0, options.limit);

if (selectedDemoPaths.length === 0) {
  throw new Error(`No .dem files found under ${demoRoot}.`);
}

console.log(
  `pro-loadout scan: demos=${selectedDemoPaths.length}/${allDemoPaths.length} ` +
    `workers=${options.workers} cache=${cacheDirectory}`
);

let completed = 0;
let cacheHits = 0;
let cacheMisses = 0;
const startedAt = Date.now();
const records = new Array(selectedDemoPaths.length);
let nextIndex = 0;

async function worker() {
  while (true) {
    const index = nextIndex++;
    if (index >= selectedDemoPaths.length) {
      return;
    }

    const sourcePath = selectedDemoPaths[index];
    const source = await sourceMetadata(demoRoot, sourcePath);
    const cachePath = join(cacheDirectory, `${source.cacheKey}.json`);
    let record = options.force
      ? null
      : await readFreshCache(cachePath, source, converterSha256);
    const cacheHit =
      record !== null && (record.status === "ok" || !options.retryFailures);
    if (cacheHit) {
      cacheHits++;
    } else if (options.cachedOnly) {
      records[index] = null;
      completed++;
      cacheMisses++;
      continue;
    } else {
      record = await parseDemo(
        sourcePath,
        source,
        converterPath,
        converterSha256
      );
      await writeJson(cachePath, record);
    }

    records[index] = record;
    completed++;
    if (
      cacheHit &&
      completed !== selectedDemoPaths.length &&
      completed % 50 !== 0
    ) {
      continue;
    }
    const elapsedSeconds = Math.max(0.001, (Date.now() - startedAt) / 1000);
    const status = record.status === "ok"
      ? `ok weapons=${record.evidence.weapons.length} ` +
        `stickers=${record.evidence.weapons.reduce(
          (sum, weapon) => sum + weapon.stickers.length,
          0
        )}`
      : `failed ${oneLine(record.error)}`;
    console.log(
      `[${completed}/${selectedDemoPaths.length}] ${cacheHit ? "cached " : ""}${status} ` +
        `${record.elapsedSeconds.toFixed(2)}s ` +
        `${(completed / elapsedSeconds).toFixed(2)} demos/s ` +
        `${source.relativePath}`
    );
  }
}

await Promise.all(
  Array.from(
    { length: Math.min(options.workers, selectedDemoPaths.length) },
    () => worker()
  )
);

const retainedRecords = records.filter((record) => record !== null);
const { catalog, report } = aggregateRecords(
  retainedRecords,
  converterSha256,
  allDemoPaths.length,
  cacheHits,
  cacheMisses
);
await writeJson(reportPath, report);
if (
  catalog.source.parsedDemoFiles === 0 ||
  catalog.weapons.length === 0 ||
  catalog.knives.length === 0 ||
  catalog.gloves.length === 0 ||
  catalog.outfits.length === 0
) {
  throw new Error(
    `No complete pro-loadout catalog could be built; see ${reportPath}.`
  );
}
await writeJson(outputPath, catalog);

console.log(
  `wrote ${outputPath}: ${catalog.weapons.length} weapon groups, ` +
    `${catalog.source.weaponTemplates} weapon templates, ` +
    `${catalog.outfits.length} knife/glove outfits`
);
console.log(
  `wrote ${reportPath}: parsed=${catalog.source.parsedDemoFiles} ` +
    `failed=${catalog.source.failedDemoFiles} owners=${catalog.source.distinctOwners}`
);

function parseArguments(args) {
  const parsed = {
    workers: 2,
    force: false,
    retryFailures: false,
    cachedOnly: false
  };
  for (let index = 0; index < args.length; index++) {
    const argument = args[index];
    if (argument === "--force") {
      parsed.force = true;
      continue;
    }
    if (argument === "--retry-failures") {
      parsed.retryFailures = true;
      continue;
    }
    if (argument === "--cached-only") {
      parsed.cachedOnly = true;
      continue;
    }
    const value = args[++index];
    if (value === undefined) {
      throw new Error(`Missing value for ${argument}.`);
    }
    switch (argument) {
      case "--demo-root":
        parsed.demoRoot = value;
        break;
      case "--converter":
        parsed.converter = value;
        break;
      case "--output":
        parsed.output = value;
        break;
      case "--report":
        parsed.report = value;
        break;
      case "--cache-dir":
        parsed.cacheDir = value;
        break;
      case "--workers":
        parsed.workers = positiveInteger(value, argument);
        break;
      case "--limit":
        parsed.limit = positiveInteger(value, argument);
        break;
      case "--match":
        parsed.match = value;
        try {
          new RegExp(value, "i");
        } catch (error) {
          throw new Error(`--match is not a valid regular expression: ${error.message}`);
        }
        break;
      default:
        throw new Error(`Unknown argument ${argument}.`);
    }
  }

  for (const required of ["demoRoot", "converter", "output"]) {
    if (parsed[required] === undefined) {
      throw new Error(
        "Usage: node tools/build-pro-loadout-catalog.mjs " +
          "--demo-root <demo-dir> --converter <cs2-demotracer.exe> " +
          "--output <offline-demo-evidence.json> [--report <report.json>] " +
          "[--cache-dir <dir>] [--workers <n>] [--limit <n>] " +
          "[--match <regex>] " +
          "[--force] [--retry-failures] [--cached-only]"
      );
    }
  }
  if (!parsed.output.toLowerCase().endsWith(".json")) {
    throw new Error("--output must end in .json.");
  }
  if (parsed.force && parsed.cachedOnly) {
    throw new Error("--force and --cached-only cannot be used together.");
  }
  return parsed;
}

function positiveInteger(value, option) {
  const parsed = Number.parseInt(value, 10);
  if (!Number.isSafeInteger(parsed) || parsed <= 0) {
    throw new Error(`${option} must be a positive integer.`);
  }
  return parsed;
}

async function findDemoFiles(root) {
  const files = [];
  const pending = [root];
  while (pending.length > 0) {
    const directory = pending.pop();
    const entries = await readdir(directory, { withFileTypes: true });
    entries.sort((left, right) => left.name.localeCompare(right.name, "en"));
    for (const entry of entries) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) {
        pending.push(path);
      } else if (entry.isFile() && entry.name.toLowerCase().endsWith(".dem")) {
        files.push(path);
      }
    }
  }
  return files.sort((left, right) =>
    relative(root, left).localeCompare(relative(root, right), "en")
  );
}

async function sourceMetadata(root, sourcePath) {
  const metadata = await stat(sourcePath);
  const relativePath = relative(root, sourcePath).split(sep).join("/");
  return {
    relativePath,
    sizeBytes: metadata.size,
    modifiedMilliseconds: Math.trunc(metadata.mtimeMs),
    cacheKey: sha256(relativePath).slice(0, 32)
  };
}

async function readFreshCache(cachePath, source, expectedConverterSha256) {
  try {
    const record = JSON.parse(await readFile(cachePath, "utf8"));
    if (
      record.cacheVersion === CACHE_VERSION &&
      record.converterSha256 === expectedConverterSha256 &&
      record.source.relativePath === source.relativePath &&
      record.source.sizeBytes === source.sizeBytes &&
      record.source.modifiedMilliseconds === source.modifiedMilliseconds
    ) {
      return record;
    }
  } catch (error) {
    if (error?.code !== "ENOENT") {
      console.warn(`ignoring invalid cache ${cachePath}: ${oneLine(error.message)}`);
    }
  }
  return null;
}

async function parseDemo(sourcePath, source, converter, converterSha256) {
  const startedAt = Date.now();
  const jobRoot = await mkdtemp(join(tmpdir(), "cs2-pro-loadout-"));
  const converterOutput = join(jobRoot, "output");
  try {
    const parsePath = /-p\d+\.dem$/i.test(sourcePath)
      ? await isolatedSegmentPath(sourcePath, jobRoot)
      : sourcePath;
    await mkdir(converterOutput, { recursive: true });
    await execFileAsync(
      converter,
      [
        "convert",
        "--demo",
        parsePath,
        "--output",
        converterOutput,
        "--include-suspicious",
        "--export-cosmetics",
        "--export-stickers",
        "--acknowledge-cosmetic-gslt-risk",
        "--accept-cosmetic-export-disclaimer"
      ],
      {
        windowsHide: true,
        maxBuffer: 8 * 1024 * 1024,
        timeout: CONVERTER_TIMEOUT_MILLISECONDS,
        killSignal: "SIGKILL"
      }
    );
    const manifestPath = await findSingleManifest(converterOutput);
    const manifest = parseManifestJson(await readFile(manifestPath, "utf8"));
    validateManifest(manifest);
    return {
      cacheVersion: CACHE_VERSION,
      converterSha256,
      status: "ok",
      source,
      elapsedSeconds: (Date.now() - startedAt) / 1000,
      demo: {
        sha256: manifest.demo_sha256,
        map: manifest.map,
        id: manifest.demo_id
      },
      evidence: extractEvidence(manifest)
    };
  } catch (error) {
    return {
      cacheVersion: CACHE_VERSION,
      converterSha256,
      status: "failed",
      source,
      elapsedSeconds: (Date.now() - startedAt) / 1000,
      error: oneLine(error.stderr || error.message || String(error))
    };
  } finally {
    await rm(jobRoot, { recursive: true, force: true });
  }
}

async function isolatedSegmentPath(sourcePath, jobRoot) {
  const isolatedPath = join(jobRoot, "source.dem");
  try {
    await link(sourcePath, isolatedPath);
  } catch (error) {
    if (!["EPERM", "EXDEV", "EACCES"].includes(error?.code)) {
      throw error;
    }
    await copyFile(sourcePath, isolatedPath);
  }
  return isolatedPath;
}

async function findSingleManifest(root) {
  const matches = [];
  const pending = [root];
  while (pending.length > 0) {
    const directory = pending.pop();
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) {
        pending.push(path);
      } else if (entry.isFile() && entry.name === "manifest.json") {
        matches.push(path);
      }
    }
  }
  if (matches.length !== 1) {
    throw new Error(`Expected one manifest.json, found ${matches.length}.`);
  }
  return matches[0];
}

function extractEvidence(manifest) {
  const weaponItems = new Map();
  const playerEvidence = new Map();
  const owners = new Set();
  let weaponConflicts = 0;
  let stickerConflicts = 0;

  for (const file of manifest.files ?? []) {
    const steamId = integerString(file.steam_id);
    if (steamId === null) {
      continue;
    }
    const playerHash = hashOwner(steamId);
    owners.add(playerHash);
    const cosmetics = file.cosmetics;
    if (cosmetics === undefined || cosmetics === null) {
      continue;
    }

    const player = playerEvidence.get(playerHash) ?? {
      playerHash,
      knives: new Map(),
      gloves: new Map(),
      outfits: new Map()
    };
    playerEvidence.set(playerHash, player);

    const knife = normalizeItemCosmetic(cosmetics.knife);
    const glove = normalizeItemCosmetic(cosmetics.glove);
    if (knife !== null) {
      player.knives.set(stableJson(knife), knife);
    }
    if (glove !== null) {
      player.gloves.set(stableJson(glove), glove);
    }
    if (knife !== null && glove !== null) {
      const outfit = { knife, glove };
      player.outfits.set(stableJson(outfit), outfit);
    }

    for (const rawWeapon of cosmetics.weapons ?? []) {
      const normalized = normalizeWeapon(rawWeapon);
      if (normalized === null) {
        continue;
      }
      const ownerSteamId = weaponOwnerIdentifier(rawWeapon, steamId);
      const ownerHash = hashOwner(ownerSteamId);
      owners.add(ownerHash);
      const itemId = integerString(rawWeapon.item_id);
      const identity = itemId === null || itemId === "0"
        ? `spec:${ownerHash}:${stableJson(normalized.base)}`
        : `item:${itemId}`;
      const observation = weaponItems.get(identity);
      if (observation === undefined) {
        weaponItems.set(identity, {
          ownerHash,
          base: normalized.base,
          stickerSets: new Map(
            normalized.stickers.length === 0
              ? []
              : [[stableJson(normalized.stickers), normalized.stickers]]
          )
        });
        continue;
      }
      if (stableJson(observation.base) !== stableJson(normalized.base)) {
        observation.weaponConflict = true;
        weaponConflicts++;
        continue;
      }
      if (normalized.stickers.length > 0) {
        observation.stickerSets.set(
          stableJson(normalized.stickers),
          normalized.stickers
        );
        if (observation.stickerSets.size > 1) {
          stickerConflicts++;
        }
      }
    }
  }

  const uniqueWeapons = new Map();
  for (const observation of weaponItems.values()) {
    if (observation.weaponConflict) {
      continue;
    }
    const stickers = observation.stickerSets.size === 1
      ? observation.stickerSets.values().next().value
      : [];
    const weapon = {
      ownerHash: observation.ownerHash,
      ...observation.base,
      stickers
    };
    uniqueWeapons.set(stableJson(weapon), weapon);
  }
  const weapons = [...uniqueWeapons.values()];

  return {
    owners: [...owners].sort(),
    weapons: weapons.sort(compareWeaponEvidence),
    knives: flattenPlayerItems(playerEvidence, "knives"),
    gloves: flattenPlayerItems(playerEvidence, "gloves"),
    outfits: flattenPlayerItems(playerEvidence, "outfits"),
    weaponConflicts,
    stickerConflicts
  };
}

function parseManifestJson(source) {
  const losslessIdentifiers = source.replace(
    /("(?:steam_id|original_owner_steam_id|item_id)"\s*:\s*)(\d{16,})/g,
    '$1"$2"'
  );
  return JSON.parse(losslessIdentifiers);
}

function validateManifest(manifest) {
  if (
    manifest?.format_version !== 8 ||
    typeof manifest.demo_id !== "string" ||
    manifest.demo_id.length === 0 ||
    !/^[0-9a-f]{64}$/i.test(manifest.demo_sha256 ?? "") ||
    typeof manifest.map !== "string" ||
    manifest.map.length === 0 ||
    !Array.isArray(manifest.files)
  ) {
    throw new Error("cs2-demotracer returned an invalid v8 manifest.");
  }
}

function normalizeWeapon(rawWeapon) {
  const weaponDefIndex = positiveIntegerOrNull(rawWeapon?.weapon_def_index);
  const paintKit = positiveIntegerOrNull(rawWeapon?.paint_kit);
  const seed = nonNegativeIntegerOrNull(rawWeapon?.seed);
  const wear = finiteUnitFloat(rawWeapon?.wear);
  if (
    weaponDefIndex === null ||
    paintKit === null ||
    seed === null ||
    wear === null
  ) {
    return null;
  }
  const stickers = (rawWeapon.stickers ?? [])
    .map(normalizeSticker)
    .filter((sticker) => sticker !== null)
    .sort((left, right) => left.slot - right.slot);
  if (new Set(stickers.map((sticker) => sticker.slot)).size !== stickers.length) {
    return null;
  }
  return {
    base: {
      weaponDefIndex,
      paintKit,
      seed,
      wear
    },
    stickers
  };
}

function normalizeSticker(rawSticker) {
  const slot = nonNegativeIntegerOrNull(rawSticker?.slot);
  const defIndex = positiveIntegerOrNull(rawSticker?.sticker_id);
  const wear = finiteUnitFloat(rawSticker?.wear);
  const x = finiteFloat(rawSticker?.offset_x);
  const y = finiteFloat(rawSticker?.offset_y);
  const scale = optionalFiniteFloat(rawSticker?.scale);
  const rotation = optionalFiniteFloat(rawSticker?.rotation);
  if (
    slot === null ||
    slot > 4 ||
    defIndex === null ||
    wear === null ||
    x === null ||
    y === null ||
    scale === undefined ||
    rotation === undefined
  ) {
    return null;
  }
  return compactObject({
    slot,
    defIndex,
    wear,
    x,
    y,
    scale,
    rotation
  });
}

function normalizeItemCosmetic(rawItem) {
  if (rawItem === undefined || rawItem === null) {
    return null;
  }
  const defIndex = positiveIntegerOrNull(rawItem.item_def_index);
  const paintKit = positiveIntegerOrNull(rawItem.paint_kit);
  const seed = nonNegativeIntegerOrNull(rawItem.seed);
  const wear = finiteUnitFloat(rawItem.wear);
  if (
    defIndex === null ||
    paintKit === null ||
    seed === null ||
    wear === null
  ) {
    return null;
  }
  return { defIndex, paintKit, seed, wear };
}

function flattenPlayerItems(playerEvidence, property) {
  const rows = [];
  for (const player of playerEvidence.values()) {
    for (const value of player[property].values()) {
      rows.push({ ownerHash: player.playerHash, ...value });
    }
  }
  return rows.sort((left, right) =>
    stableJson(left).localeCompare(stableJson(right), "en")
  );
}

function aggregateRecords(
  records,
  converterSha256,
  discoveredDemoFiles,
  cacheHits,
  cacheMisses
) {
  const successful = records.filter((record) => record.status === "ok");
  const failed = records.filter((record) => record.status !== "ok");
  const owners = new Set();
  const maps = new Set();
  const weaponTemplates = new Map();
  const knifeTemplates = new Map();
  const gloveTemplates = new Map();
  const outfitTemplates = new Map();
  let weaponConflicts = 0;
  let stickerConflicts = 0;

  for (const record of successful) {
    const demoHash = record.demo.sha256;
    maps.add(record.demo.map);
    for (const owner of record.evidence.owners) {
      owners.add(owner);
    }
    weaponConflicts += record.evidence.weaponConflicts;
    stickerConflicts += record.evidence.stickerConflicts;
    for (const weapon of record.evidence.weapons) {
      addAggregate(weaponTemplates, weapon, demoHash);
    }
    for (const knife of record.evidence.knives) {
      addAggregate(knifeTemplates, knife, demoHash);
    }
    for (const glove of record.evidence.gloves) {
      addAggregate(gloveTemplates, glove, demoHash);
    }
    for (const outfit of record.evidence.outfits) {
      addAggregate(outfitTemplates, outfit, demoHash);
    }
  }

  const weaponGroups = new Map();
  for (const aggregate of weaponTemplates.values()) {
    const template = finalizeAggregate(aggregate);
    const group = weaponGroups.get(template.weaponDefIndex) ?? [];
    group.push(template);
    weaponGroups.set(template.weaponDefIndex, group);
  }
  const weapons = [...weaponGroups]
    .sort(([left], [right]) => left - right)
    .map(([weaponDefIndex, templates]) => ({
      weaponDefIndex,
      templates: templates
        .map(({ weaponDefIndex: _ignored, ...template }) => template)
        .sort(compareWeightedTemplate)
    }));

  const sourceBytes = records.reduce(
    (sum, record) => sum + record.source.sizeBytes,
    0
  );
  const corpusDigest = sha256(
    records
      .map((record) =>
        [
          record.source.relativePath,
          record.source.sizeBytes,
          record.source.modifiedMilliseconds,
          record.status === "ok" ? record.demo.sha256 : "failed"
        ].join("\0")
      )
      .sort()
      .join("\n")
  );
  const stickeredTemplates = [...weaponTemplates.values()].filter(
    (aggregate) => aggregate.value.stickers.length > 0
  );
  const stickerCount = stickeredTemplates.reduce(
    (sum, aggregate) =>
      sum + aggregate.value.stickers.length * aggregate.observations,
    0
  );

  const source = {
    format: "cs2-demotracer-manifest-v8",
    weighting: "distinct-owners",
    converterSha256,
    corpusDigest,
    discoveredDemoFiles,
    selectedDemoFiles: records.length,
    parsedDemoFiles: successful.length,
    failedDemoFiles: failed.length,
    sourceBytes,
    mapCount: maps.size,
    distinctOwners: owners.size,
    weaponTemplates: weaponTemplates.size,
    stickeredWeaponTemplates: stickeredTemplates.length,
    stickerObservations: stickerCount,
    knifeTemplates: knifeTemplates.size,
    gloveTemplates: gloveTemplates.size,
    outfitTemplates: outfitTemplates.size
  };

  const catalog = {
    version: CATALOG_VERSION,
    source,
    weapons,
    knives: finalizeCollection(knifeTemplates),
    gloves: finalizeCollection(gloveTemplates),
    outfits: finalizeCollection(outfitTemplates)
  };
  const report = {
    version: CATALOG_VERSION,
    source,
    cacheHits,
    cacheMisses,
    elapsedSeconds: (Date.now() - startedAt) / 1000,
    conflicts: {
      weapon: weaponConflicts,
      sticker: stickerConflicts
    },
    failures: failed.map((record) => ({
      source: record.source.relativePath,
      sizeBytes: record.source.sizeBytes,
      elapsedSeconds: record.elapsedSeconds,
      error: record.error
    })),
    coverage: {
      weaponObservationCount: sumObservations(weaponTemplates),
      knifeObservationCount: sumObservations(knifeTemplates),
      gloveObservationCount: sumObservations(gloveTemplates),
      outfitObservationCount: sumObservations(outfitTemplates)
    }
  };
  return { catalog, report };
}

function addAggregate(target, rawValue, demoHash) {
  const { ownerHash, ...value } = rawValue;
  const key = stableJson(value);
  const aggregate = target.get(key) ?? {
    value,
    observations: 0,
    owners: new Set(),
    demos: new Set()
  };
  aggregate.observations++;
  aggregate.owners.add(ownerHash);
  aggregate.demos.add(demoHash);
  target.set(key, aggregate);
}

function finalizeCollection(collection) {
  return [...collection.values()]
    .map(finalizeAggregate)
    .sort(compareWeightedTemplate);
}

function finalizeAggregate(aggregate) {
  return {
    ...aggregate.value,
    weight: aggregate.owners.size,
    owners: aggregate.owners.size,
    demos: aggregate.demos.size,
    observations: aggregate.observations
  };
}

function compareWeightedTemplate(left, right) {
  return (
    right.weight - left.weight ||
    right.observations - left.observations ||
    stableJson(left).localeCompare(stableJson(right), "en")
  );
}

function sumObservations(collection) {
  return [...collection.values()].reduce(
    (sum, aggregate) => sum + aggregate.observations,
    0
  );
}

function compareWeaponEvidence(left, right) {
  return (
    left.weaponDefIndex - right.weaponDefIndex ||
    left.paintKit - right.paintKit ||
    left.ownerHash.localeCompare(right.ownerHash, "en") ||
    stableJson(left).localeCompare(stableJson(right), "en")
  );
}

function stableJson(value) {
  if (Array.isArray(value)) {
    return `[${value.map(stableJson).join(",")}]`;
  }
  if (value !== null && typeof value === "object") {
    return `{${Object.keys(value)
      .sort()
      .map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`)
      .join(",")}}`;
  }
  return JSON.stringify(value);
}

function compactObject(value) {
  return Object.fromEntries(
    Object.entries(value).filter(([, item]) => item !== null)
  );
}

function positiveIntegerOrNull(value) {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}

function nonNegativeIntegerOrNull(value) {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed >= 0 ? parsed : null;
}

function integerString(value) {
  if (typeof value === "bigint") {
    return value.toString();
  }
  if (typeof value === "number" && Number.isSafeInteger(value) && value >= 0) {
    return value.toString();
  }
  if (typeof value === "string" && /^\d+$/.test(value)) {
    return value;
  }
  return null;
}

function weaponOwnerIdentifier(rawWeapon, fallbackSteamId) {
  const originalOwner = integerString(rawWeapon.original_owner_steam_id);
  if (originalOwner !== null && originalOwner !== "0") {
    return originalOwner;
  }
  const accountId = integerString(rawWeapon.item_account_id);
  if (accountId !== null && accountId !== "0") {
    return (STEAM_ID64_ACCOUNT_BASE + BigInt(accountId)).toString();
  }
  return fallbackSteamId;
}

function finiteFloat(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function finiteUnitFloat(value) {
  const parsed = finiteFloat(value);
  return parsed !== null && parsed >= 0 && parsed <= 1 ? parsed : null;
}

function optionalFiniteFloat(value) {
  if (value === undefined || value === null) {
    return null;
  }
  const parsed = finiteFloat(value);
  return parsed === null ? undefined : parsed;
}

function hashOwner(steamId) {
  return sha256(`${OWNER_HASH_DOMAIN}${steamId}`);
}

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

async function sha256File(path) {
  return sha256(await readFile(path));
}

async function writeJson(path, value) {
  const temporaryPath =
    `${path}.${process.pid}.${Date.now()}.${Math.random().toString(16).slice(2)}.tmp`;
  try {
    await writeFile(
      temporaryPath,
      `${JSON.stringify(value, null, 2)}\n`,
      "utf8"
    );
    await rename(temporaryPath, path);
  } finally {
    await rm(temporaryPath, { force: true });
  }
}

function oneLine(value) {
  return String(value).replace(/\s+/g, " ").trim().slice(0, 1000);
}
