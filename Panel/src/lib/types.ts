export type FilterMode = "allow" | "deny";

export type SelectionFilter<T> = {
  mode: FilterMode;
  items: T[];
};

export type VariantKey = {
  defIndex: number;
  paintKit: number;
};

export type RandomizerOptions = {
  enabled: boolean;
  weaponMode: "persistent" | "kaleidoscope";
  weapons: boolean;
  knives: boolean;
  gloves: boolean;
  agents: boolean;
  music: boolean;
  stickers: boolean;
  charms: boolean;
};

export type RandomizerFilters = {
  knifeTypes: SelectionFilter<number>;
  weaponPaints: SelectionFilter<VariantKey>;
  knifePaints: SelectionFilter<VariantKey>;
  gloves: SelectionFilter<VariantKey>;
  stickers: SelectionFilter<number>;
  charms: SelectionFilter<number>;
  agents: SelectionFilter<string>;
  musicKits: SelectionFilter<number>;
};

export type RandomizerConfig = {
  schemaVersion: 1;
  options: RandomizerOptions;
  filters: RandomizerFilters;
};

export type DisplayFields = {
  id: number;
  name: string;
  nameZh: string;
  category?: string;
  categoryZh?: string;
  collection?: string;
  collectionZh?: string;
  image?: string;
  rarity?: string;
};

export type PaintCatalogEntry = DisplayFields & {
  paintKit: number;
  legacy: boolean;
  wearMin: number;
  wearMax: number;
};

export type WeaponCatalogEntry = DisplayFields & {
  designerName: string;
  defIndex: number;
  team?: number;
  stickerSchemaCount: number;
  legacyStickerSchemaCount: number;
  paints: PaintCatalogEntry[];
};

export type KnifeCatalogEntry = DisplayFields & {
  designerName: string;
  defIndex: number;
  paints: PaintCatalogEntry[];
};

export type GloveCatalogEntry = DisplayFields & {
  defIndex: number;
  paintKit: number;
  wearMin: number;
  wearMax: number;
};

export type IndexedCatalogEntry = DisplayFields & {
  index: number;
};

export type AgentCatalogEntry = DisplayFields & {
  defIndex: number;
  team: 2 | 3;
  modelPath: string;
};

export type CosmeticCatalog = {
  schemaVersion: 2;
  source: {
    repository: "ianlucas/cs2-lib";
    commit: string;
    assetsBaseUrl: string;
  };
  weapons: WeaponCatalogEntry[];
  knives: KnifeCatalogEntry[];
  gloves: GloveCatalogEntry[];
  stickerKits: number[];
  stickerItems: IndexedCatalogEntry[];
  keychainDefinitions: number[];
  keychainItems: IndexedCatalogEntry[];
  musicKits: number[];
  musicKitItems: IndexedCatalogEntry[];
  agents: AgentCatalogEntry[];
};

export type WorkspacePayload = {
  pluginDirectory: string;
  configPath: string;
  config: RandomizerConfig;
  catalog: CosmeticCatalog;
};

export type FilterItemMap = {
  knifeTypes: number;
  weaponPaints: VariantKey;
  knifePaints: VariantKey;
  gloves: VariantKey;
  stickers: number;
  charms: number;
  agents: string;
  musicKits: number;
};

export type FilterKey = keyof FilterItemMap;
