import { useEffect, useMemo, useState } from "react";
import { open } from "@tauri-apps/plugin-dialog";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { api } from "./lib/api";
import type {
  AgentCatalogEntry,
  CosmeticCatalog,
  DisplayFields,
  FilterItemMap,
  FilterKey,
  FilterMode,
  GloveCatalogEntry,
  IndexedCatalogEntry,
  KnifeCatalogEntry,
  RandomizerConfig,
  RandomizerFilters,
  SelectionFilter,
  VariantKey,
  WeaponCatalogEntry,
  WorkspacePayload
} from "./lib/types";

type View =
  | "overview"
  | "secondary"
  | "smg"
  | "heavy"
  | "rifle"
  | "equipment"
  | "knives"
  | "gloves"
  | "stickers"
  | "charms"
  | "agents"
  | "music";

type FilterValue = number | string | VariantKey;
type BooleanOptionKey = Exclude<keyof RandomizerConfig["options"], "weaponMode">;

type AssetCandidate = {
  key: string;
  value: FilterValue;
  display: DisplayFields;
  eyebrow: string;
  detail?: string;
  trailing?: string;
};

const VIEW_LABELS: Record<View, string> = {
  overview: "总览",
  secondary: "手枪",
  smg: "微型冲锋枪",
  heavy: "重型武器",
  rifle: "步枪",
  equipment: "装备",
  knives: "匕首",
  gloves: "手套",
  stickers: "印花",
  charms: "挂件",
  agents: "探员",
  music: "音乐盒"
};

const RARITY_NAMES: Record<string, string> = {
  "#ded6cc": "基础",
  "#b0c3d9": "消费级",
  "#5e98d9": "工业级",
  "#4b69ff": "军规级",
  "#8847ff": "受限",
  "#d32ce6": "保密",
  "#eb4b4b": "隐秘",
  "#e4ae39": "非凡"
};

const OPTION_LABELS: { key: BooleanOptionKey; label: string; note: string }[] = [
  { key: "enabled", label: "Randomizer 总开关", note: "控制插件是否向 Bot 写入任何随机外观" },
  { key: "weapons", label: "武器涂装", note: "枪械皮肤、磨损与稳定随机结果" },
  { key: "knives", label: "刀具", note: "刀型与对应刀具涂装" },
  { key: "gloves", label: "手套", note: "手套型号和涂装组合" },
  { key: "agents", label: "探员", note: "按阵营随机探员模型" },
  { key: "music", label: "音乐盒", note: "MVP 音乐盒" },
  { key: "stickers", label: "印花", note: "最多五张印花及动态位置" },
  { key: "charms", label: "挂件", note: "挂件与本地观测位置" }
];

function valueToken(value: unknown): string {
  if (typeof value === "object" && value !== null && "defIndex" in value && "paintKit" in value) {
    const variant = value as VariantKey;
    return `${variant.defIndex}:${variant.paintKit}`;
  }
  return String(value);
}

function cloneConfig(config: RandomizerConfig): RandomizerConfig {
  return structuredClone(config);
}

function imageUrl(catalog: CosmeticCatalog, image?: string): string | undefined {
  if (!image) return undefined;
  return image.startsWith("http") ? image : `${catalog.source.assetsBaseUrl}${image}`;
}

function weaponCategory(weapon: WeaponCatalogEntry): "secondary" | "smg" | "heavy" | "rifle" | "equipment" | null {
  const category = weapon.category;
  if (category === "secondary" || category === "smg" || category === "heavy" || category === "rifle" || category === "equipment") {
    return category;
  }
  if ([1, 2, 3, 4, 30, 32, 36, 61, 63, 64].includes(weapon.defIndex)) return "secondary";
  if ([17, 19, 23, 24, 26, 33, 34].includes(weapon.defIndex)) return "smg";
  if ([14, 25, 27, 28, 29, 35].includes(weapon.defIndex)) return "heavy";
  if ([7, 8, 9, 10, 11, 13, 16, 38, 39, 40, 60].includes(weapon.defIndex)) return "rifle";
  return null;
}

function ModeSwitch({ mode, onChange }: { mode: FilterMode; onChange: (mode: FilterMode) => void }) {
  return (
    <div className="mode-switch" role="group" aria-label="过滤模式">
      <button className={mode === "allow" ? "is-active" : ""} onClick={() => onChange("allow")}>
        白名单
      </button>
      <button className={mode === "deny" ? "is-active is-deny" : ""} onClick={() => onChange("deny")}>
        黑名单
      </button>
    </div>
  );
}

function Toggle({ checked, onChange }: { checked: boolean; onChange: (checked: boolean) => void }) {
  return (
    <button
      className={`toggle ${checked ? "is-on" : ""}`}
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
    >
      <span />
    </button>
  );
}

function AssetImage({ src, alt }: { src?: string; alt: string }) {
  const [failed, setFailed] = useState(false);
  if (!src || failed) return <div className="asset-image asset-image--missing">BR</div>;
  return <img className="asset-image" src={src} alt={alt} draggable={false} onError={() => setFailed(true)} />;
}

function AssetList({
  title,
  subtitle,
  mode,
  candidates,
  catalog,
  selectedTokens,
  onMode,
  onToggle,
  onMarkScope,
  emptyMessage = "没有符合条件的饰品"
}: {
  title: string;
  subtitle: string;
  mode: FilterMode;
  candidates: AssetCandidate[];
  catalog: CosmeticCatalog;
  selectedTokens: Set<string>;
  onMode: (mode: FilterMode) => void;
  onToggle: (value: FilterValue) => void;
  onMarkScope: (values: FilterValue[], marked: boolean) => void;
  emptyMessage?: string;
}) {
  const [query, setQuery] = useState("");
  const [rarity, setRarity] = useState<string | null>(null);
  const normalized = query.trim().toLocaleLowerCase();
  const rarities = useMemo(
    () => [...new Set(candidates.map((item) => item.display.rarity).filter(Boolean) as string[])],
    [candidates]
  );
  const filtered = useMemo(
    () =>
      candidates.filter((item) => {
        if (rarity && item.display.rarity !== rarity) return false;
        if (!normalized) return true;
        return [
          item.display.name,
          item.display.nameZh,
          item.display.category,
          item.display.categoryZh,
          item.display.collection,
          item.display.collectionZh,
          item.eyebrow,
          item.detail,
          item.trailing
        ].some((value) => value?.toLocaleLowerCase().includes(normalized));
      }),
    [candidates, normalized, rarity]
  );
  const visible = filtered.slice(0, 180);
  const markedInScope = candidates.filter((item) => selectedTokens.has(valueToken(item.value))).length;

  return (
    <section className="inventory-panel glass">
      <div className="inventory-head">
        <div>
          <div className="section-kicker">RANDOM POOL</div>
          <h2>{title}</h2>
          <p>{subtitle}</p>
        </div>
        <div className="filter-summary">
          <span className={`mode-badge mode-badge--${mode}`}>{mode === "allow" ? "仅随机已选" : "排除已选"}</span>
          <strong>{markedInScope}</strong>
          <span>/ {candidates.length} 项已标记</span>
        </div>
      </div>

      <div className="filter-toolbar">
        <ModeSwitch mode={mode} onChange={onMode} />
        <label className="search-box">
          <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="6" /><path d="m16 16 4 4" /></svg>
          <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="搜索名称、系列或编号" />
        </label>
        <button className="quiet-button" onClick={() => onMarkScope(filtered.map((item) => item.value), true)}>
          标记当前结果
        </button>
        <button className="quiet-button" onClick={() => onMarkScope(candidates.map((item) => item.value), false)}>
          清空本页
        </button>
      </div>

      {rarities.length > 1 && (
        <div className="rarity-strip">
          <button className={!rarity ? "is-active" : ""} onClick={() => setRarity(null)}>全部稀有度</button>
          {rarities.map((color) => (
            <button key={color} className={rarity === color ? "is-active" : ""} onClick={() => setRarity(color)}>
              <i style={{ background: color }} />{RARITY_NAMES[color] ?? color}
            </button>
          ))}
        </div>
      )}

      <div className="asset-list">
        {visible.map((item) => {
          const marked = selectedTokens.has(valueToken(item.value));
          return (
            <button
              className={`asset-row ${marked ? `is-marked is-${mode}` : ""}`}
              key={item.key}
              onClick={() => onToggle(item.value)}
              aria-pressed={marked}
            >
              <div className="asset-row__rarity" style={{ background: item.display.rarity ?? "#8e8e93" }} />
              <AssetImage src={imageUrl(catalog, item.display.image)} alt={item.display.nameZh || item.display.name} />
              <div className="asset-row__copy">
                <span className="asset-row__eyebrow">{item.eyebrow}</span>
                <strong style={{ color: item.display.rarity ?? undefined }}>
                  {item.display.nameZh || item.display.name}
                </strong>
                {item.detail && <small>{item.detail}</small>}
              </div>
              {item.trailing && <code>{item.trailing}</code>}
              <span className="asset-row__state">
                {marked ? (mode === "allow" ? "在随机池中" : "已排除") : mode === "allow" ? "未放行" : "可随机"}
              </span>
            </button>
          );
        })}
        {visible.length === 0 && <div className="empty-list">{emptyMessage}</div>}
      </div>
      {filtered.length > visible.length && (
        <div className="list-cap">为保持流畅仅显示前 {visible.length} 项，请继续缩小搜索范围。</div>
      )}
    </section>
  );
}

function Overview({
  workspace,
  draft,
  onOption,
  onWeaponMode
}: {
  workspace: WorkspacePayload;
  draft: RandomizerConfig;
  onOption: (key: BooleanOptionKey, checked: boolean) => void;
  onWeaponMode: (mode: RandomizerConfig["options"]["weaponMode"]) => void;
}) {
  const catalog = workspace.catalog;
  const stats = [
    ["武器涂装", catalog.weapons.reduce((sum, weapon) => sum + weapon.paints.length, 0)],
    ["刀具涂装", catalog.knives.reduce((sum, knife) => sum + knife.paints.length, 0)],
    ["手套", catalog.gloves.length],
    ["印花", catalog.stickerItems.length],
    ["挂件", catalog.keychainItems.length],
    ["探员", catalog.agents.length]
  ];
  return (
    <div className="overview-grid">
      <section className="hero-card glass">
        <div className="hero-card__mark">BR</div>
        <div>
          <div className="section-kicker">LOCAL CONTROL PLANE</div>
          <h1>Bot Randomizer</h1>
          <p>本地配置是唯一真源。保存后插件会自动检测变更并热加载，不再需要修改刀型数组。</p>
        </div>
        <span className={`live-pill ${draft.options.enabled ? "is-live" : ""}`}>
          <i />{draft.options.enabled ? "随机器已启用" : "随机器已停用"}
        </span>
      </section>

      <section className="options-card glass">
        <div className="card-title-row">
          <div><div className="section-kicker">RUNTIME</div><h2>功能范围</h2></div>
          <span>写入 randomizer_config.json</span>
        </div>
        <div className="weapon-mode">
          <div>
            <strong>武器随机模式</strong>
            <span>只影响枪械涂装、印花与挂件；刀和手套始终保持稳定。</span>
          </div>
          <div className="weapon-mode__switch" role="group" aria-label="武器随机模式">
            <button
              className={draft.options.weaponMode === "persistent" ? "is-active" : ""}
              onClick={() => onWeaponMode("persistent")}
            >
              <strong>默认持久化</strong><small>同一 Bot 的同型号武器保持一致</small>
            </button>
            <button
              className={draft.options.weaponMode === "kaleidoscope" ? "is-active is-kaleidoscope" : ""}
              onClick={() => onWeaponMode("kaleidoscope")}
            >
              <strong>万花筒</strong><small>每次新武器都重新随机整套搭配</small>
            </button>
          </div>
        </div>
        <div className="option-list">
          {OPTION_LABELS.map((option) => (
            <div className="option-row" key={option.key}>
              <div><strong>{option.label}</strong><span>{option.note}</span></div>
              <Toggle checked={draft.options[option.key]} onChange={(checked) => onOption(option.key, checked)} />
            </div>
          ))}
        </div>
      </section>

      <section className="source-card glass">
        <div className="card-title-row">
          <div><div className="section-kicker">IAN LUCAS DATA</div><h2>本地静态资源</h2></div>
          <span className="verified-pill">已校验</span>
        </div>
        <div className="source-path selectable">{workspace.configPath}</div>
        <div className="stat-grid">
          {stats.map(([label, value]) => <div key={label}><strong>{value.toLocaleString()}</strong><span>{label}</span></div>)}
        </div>
        <div className="source-meta">
          <span>ianlucas/cs2-lib</span>
          <code>{catalog.source.commit.slice(0, 12)}</code>
          <span>中英文名称 · 稀有度 · 归属 · CDN 缩略图</span>
        </div>
      </section>
    </div>
  );
}

function WeaponView({
  category,
  catalog,
  filter,
  onMode,
  onToggle,
  onMarkScope
}: {
  category: "secondary" | "smg" | "heavy" | "rifle" | "equipment";
  catalog: CosmeticCatalog;
  filter: SelectionFilter<VariantKey>;
  onMode: (mode: FilterMode) => void;
  onToggle: (value: FilterValue) => void;
  onMarkScope: (values: FilterValue[], marked: boolean) => void;
}) {
  const weapons = useMemo(
    () => catalog.weapons.filter((weapon) => weaponCategory(weapon) === category),
    [catalog, category]
  );
  const [activeDef, setActiveDef] = useState(() => weapons[0]?.defIndex ?? 0);
  useEffect(() => {
    if (!weapons.some((weapon) => weapon.defIndex === activeDef)) setActiveDef(weapons[0]?.defIndex ?? 0);
  }, [activeDef, weapons]);
  const active = weapons.find((weapon) => weapon.defIndex === activeDef) ?? weapons[0];
  const selectedTokens = useMemo(() => new Set(filter.items.map(valueToken)), [filter.items]);
  const candidates: AssetCandidate[] = (active?.paints ?? []).map((paint) => ({
    key: `${active.defIndex}:${paint.paintKit}`,
    value: { defIndex: active.defIndex, paintKit: paint.paintKit },
    display: paint,
    eyebrow: active.nameZh || active.name,
    detail: paint.collectionZh || paint.collection || "无收藏品归属",
    trailing: `#${paint.paintKit}`
  }));

  return (
    <div className="catalog-layout">
      <aside className="subcatalog glass">
        <div className="subcatalog__title">{VIEW_LABELS[category]}型号</div>
        {weapons.map((weapon) => (
          <button
            key={weapon.defIndex}
            className={weapon.defIndex === active?.defIndex ? "is-active" : ""}
            onClick={() => setActiveDef(weapon.defIndex)}
          >
            <AssetImage src={imageUrl(catalog, weapon.image)} alt={weapon.nameZh || weapon.name} />
            <span>{weapon.nameZh || weapon.name}</span>
            <small>{weapon.paints.length}</small>
          </button>
        ))}
      </aside>
      <AssetList
        title={active ? `${active.nameZh || active.name} 涂装` : VIEW_LABELS[category]}
        subtitle="按 Ian Lucas 的武器归属浏览；白名单只随机标记项，黑名单跳过标记项。"
        mode={filter.mode}
        candidates={candidates}
        catalog={catalog}
        selectedTokens={selectedTokens}
        onMode={onMode}
        onToggle={onToggle}
        onMarkScope={onMarkScope}
      />
    </div>
  );
}

function KnifeView({
  catalog,
  typeFilter,
  paintFilter,
  onTypeMode,
  onPaintMode,
  onTypeToggle,
  onPaintToggle,
  onMarkPaintScope
}: {
  catalog: CosmeticCatalog;
  typeFilter: SelectionFilter<number>;
  paintFilter: SelectionFilter<VariantKey>;
  onTypeMode: (mode: FilterMode) => void;
  onPaintMode: (mode: FilterMode) => void;
  onTypeToggle: (value: number) => void;
  onPaintToggle: (value: FilterValue) => void;
  onMarkPaintScope: (values: FilterValue[], marked: boolean) => void;
}) {
  const [activeDef, setActiveDef] = useState(catalog.knives[0]?.defIndex ?? 0);
  const active = catalog.knives.find((knife) => knife.defIndex === activeDef) ?? catalog.knives[0];
  const typeTokens = useMemo(() => new Set(typeFilter.items.map(String)), [typeFilter.items]);
  const paintTokens = useMemo(() => new Set(paintFilter.items.map(valueToken)), [paintFilter.items]);
  const eligibleCount = catalog.knives.filter((knife) =>
    typeFilter.mode === "allow" ? typeTokens.has(String(knife.defIndex)) : !typeTokens.has(String(knife.defIndex))
  ).length;
  const candidates: AssetCandidate[] = (active?.paints ?? []).map((paint) => ({
    key: `${active.defIndex}:${paint.paintKit}`,
    value: { defIndex: active.defIndex, paintKit: paint.paintKit },
    display: paint,
    eyebrow: active.nameZh || active.name,
    detail: paint.collectionZh || paint.collection || `磨损 ${paint.wearMin}–${paint.wearMax}`,
    trailing: `#${paint.paintKit}`
  }));

  return (
    <div className="knife-stack">
      <section className="knife-types glass">
        <div className="inventory-head">
          <div><div className="section-kicker">KNIFE FAMILY GATE</div><h2>刀型总开关</h2><p>先按刀型整体放行或排除，再细选该刀型内的 econ 涂装。</p></div>
          <div className="knife-type-controls">
            <span><strong>{eligibleCount}</strong> / {catalog.knives.length} 种可随机</span>
            <ModeSwitch mode={typeFilter.mode} onChange={onTypeMode} />
          </div>
        </div>
        <div className="knife-grid">
          {catalog.knives.map((knife) => {
            const marked = typeTokens.has(String(knife.defIndex));
            const eligible = typeFilter.mode === "allow" ? marked : !marked;
            return (
              <div
                key={knife.defIndex}
                className={`knife-tile ${active?.defIndex === knife.defIndex ? "is-active" : ""} ${marked ? `is-marked is-${typeFilter.mode}` : ""}`}
              >
                <button className="knife-tile__preview" onClick={() => setActiveDef(knife.defIndex)}>
                  <AssetImage src={imageUrl(catalog, knife.image)} alt={knife.nameZh || knife.name} />
                  <span>{knife.nameZh || knife.name}</span>
                  <small>#{knife.defIndex} · {eligible ? "可随机" : "不进入随机池"}</small>
                </button>
                <button className="knife-tile__mark" onClick={() => onTypeToggle(knife.defIndex)}>
                  {marked ? "取消标记" : typeFilter.mode === "allow" ? "加入白名单" : "加入黑名单"}
                </button>
              </div>
            );
          })}
        </div>
      </section>
      <AssetList
        title={`${active?.nameZh || active?.name || "刀具"} · 具体涂装`}
        subtitle="这一层只处理具体 econ；排除整种刀请使用上方刀型总开关。"
        mode={paintFilter.mode}
        candidates={candidates}
        catalog={catalog}
        selectedTokens={paintTokens}
        onMode={onPaintMode}
        onToggle={onPaintToggle}
        onMarkScope={onMarkPaintScope}
      />
    </div>
  );
}

function SimpleAssetView({
  view,
  catalog,
  filter,
  candidates,
  onMode,
  onToggle,
  onMarkScope
}: {
  view: View;
  catalog: CosmeticCatalog;
  filter: SelectionFilter<unknown>;
  candidates: AssetCandidate[];
  onMode: (mode: FilterMode) => void;
  onToggle: (value: FilterValue) => void;
  onMarkScope: (values: FilterValue[], marked: boolean) => void;
}) {
  const tokens = useMemo(() => new Set(filter.items.map(valueToken)), [filter.items]);
  return (
    <AssetList
      title={`${VIEW_LABELS[view]}随机名单`}
      subtitle="名称、归属、稀有度和图片均来自本地 Ian Lucas catalog。"
      mode={filter.mode}
      candidates={candidates}
      catalog={catalog}
      selectedTokens={tokens}
      onMode={onMode}
      onToggle={onToggle}
      onMarkScope={onMarkScope}
    />
  );
}

function WindowTitleBar({ dirty }: { dirty: boolean }) {
  const appWindow = getCurrentWindow();
  return (
    <div className="titlebar" data-tauri-drag-region>
      <div className="titlebar__brand" data-tauri-drag-region><span>BR</span>BOT RANDOMIZER <i>{dirty ? "未保存" : "已同步"}</i></div>
      <div className="window-controls">
        <button aria-label="最小化" onClick={() => appWindow.minimize()}>—</button>
        <button aria-label="最大化" onClick={() => appWindow.toggleMaximize()}>□</button>
        <button className="is-close" aria-label="关闭" onClick={() => appWindow.close()}>×</button>
      </div>
    </div>
  );
}

export default function App() {
  const [workspace, setWorkspace] = useState<WorkspacePayload | null>(null);
  const [draft, setDraft] = useState<RandomizerConfig | null>(null);
  const [view, setView] = useState<View>("overview");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ kind: "ok" | "error"; text: string } | null>(null);

  useEffect(() => {
    api.loadWorkspace()
      .then((payload) => {
        if (payload) {
          setWorkspace(payload);
          setDraft(cloneConfig(payload.config));
        }
      })
      .catch((error) => setMessage({ kind: "error", text: String(error) }))
      .finally(() => setLoading(false));
  }, []);

  const dirty = !!workspace && !!draft && JSON.stringify(workspace.config) !== JSON.stringify(draft);

  const chooseDirectory = async () => {
    try {
      const selected = await open({ directory: true, multiple: false, title: "选择 BotRandomizer 插件目录" });
      if (!selected) return;
      setLoading(true);
      const payload = await api.loadWorkspace(selected);
      if (!payload) throw new Error("未能加载所选目录。");
      setWorkspace(payload);
      setDraft(cloneConfig(payload.config));
      setView("overview");
      setMessage({ kind: "ok", text: "已读取本地 randomizer_config.json。" });
    } catch (error) {
      setMessage({ kind: "error", text: String(error) });
    } finally {
      setLoading(false);
    }
  };

  const save = async () => {
    if (!workspace || !draft) return;
    setSaving(true);
    try {
      await api.saveConfig(workspace.pluginDirectory, draft);
      setWorkspace({ ...workspace, config: cloneConfig(draft) });
      setMessage({ kind: "ok", text: "配置已保存；插件将在约 1 秒内自动热加载。" });
    } catch (error) {
      setMessage({ kind: "error", text: String(error) });
    } finally {
      setSaving(false);
    }
  };

  const patchFilter = (key: FilterKey, updater: (filter: SelectionFilter<unknown>) => SelectionFilter<unknown>) => {
    setDraft((current) => {
      if (!current) return current;
      const filters = { ...current.filters } as RandomizerFilters;
      const next = updater(current.filters[key] as SelectionFilter<unknown>);
      (filters as Record<FilterKey, SelectionFilter<unknown>>)[key] = next;
      return { ...current, filters };
    });
  };

  const setFilterMode = (key: FilterKey, mode: FilterMode) =>
    patchFilter(key, (filter) => ({ ...filter, mode }));

  const toggleFilterValue = (key: FilterKey, value: FilterValue) =>
    patchFilter(key, (filter) => {
      const token = valueToken(value);
      const exists = filter.items.some((item) => valueToken(item) === token);
      return {
        ...filter,
        items: exists ? filter.items.filter((item) => valueToken(item) !== token) : [...filter.items, value]
      };
    });

  const markScope = (key: FilterKey, values: FilterValue[], marked: boolean) =>
    patchFilter(key, (filter) => {
      const scope = new Set(values.map(valueToken));
      const retained = filter.items.filter((item) => !scope.has(valueToken(item)));
      return { ...filter, items: marked ? [...retained, ...values] : retained };
    });

  const setOption = (key: BooleanOptionKey, checked: boolean) =>
    setDraft((current) => current ? { ...current, options: { ...current.options, [key]: checked } } : current);

  const setWeaponMode = (weaponMode: RandomizerConfig["options"]["weaponMode"]) =>
    setDraft((current) => current ? { ...current, options: { ...current.options, weaponMode } } : current);

  if (loading) {
    return <div className="app"><WindowTitleBar dirty={false} /><div className="boot-screen"><div className="spinner" /><strong>正在读取本地 catalog…</strong></div></div>;
  }

  if (!workspace || !draft) {
    return (
      <div className="app">
        <WindowTitleBar dirty={false} />
        <main className="welcome">
          <div className="welcome__orb">BR</div>
          <div className="section-kicker">FIRST CONNECTION</div>
          <h1>连接 BotRandomizer</h1>
          <p>选择同时包含 <code>randomizer_config.json</code> 与 <code>cosmetic_catalog.json</code> 的插件目录。Panel 会记住此位置。</p>
          <button className="primary-button" onClick={chooseDirectory}>选择插件目录</button>
          {message && <div className={`toast is-${message.kind}`}>{message.text}</div>}
        </main>
      </div>
    );
  }

  const catalog = workspace.catalog;
  const sidebarImage = (target: View) => {
    if (["secondary", "smg", "heavy", "rifle", "equipment"].includes(target)) {
      return catalog.weapons.find((weapon) => weaponCategory(weapon) === target)?.image;
    }
    if (target === "knives") return catalog.knives[4]?.image;
    if (target === "gloves") return catalog.gloves[0]?.image;
    if (target === "stickers") return catalog.stickerItems[0]?.image;
    if (target === "charms") return catalog.keychainItems[0]?.image;
    if (target === "agents") return catalog.agents[0]?.image;
    if (target === "music") return catalog.musicKitItems[0]?.image;
    return undefined;
  };

  const simpleCandidates = (target: View): AssetCandidate[] => {
    if (target === "gloves") return catalog.gloves.map((item: GloveCatalogEntry) => ({
      key: `${item.defIndex}:${item.paintKit}`,
      value: { defIndex: item.defIndex, paintKit: item.paintKit },
      display: item,
      eyebrow: "手套",
      detail: item.collectionZh || item.collection || `定义 ${item.defIndex}`,
      trailing: `#${item.paintKit}`
    }));
    if (target === "stickers") return catalog.stickerItems.map((item: IndexedCatalogEntry) => ({
      key: String(item.index), value: item.index, display: item, eyebrow: item.categoryZh || item.category || "印花",
      detail: item.collectionZh || item.collection, trailing: `#${item.index}`
    }));
    if (target === "charms") return catalog.keychainItems.map((item: IndexedCatalogEntry) => ({
      key: String(item.index), value: item.index, display: item, eyebrow: "挂件", detail: item.collectionZh || item.collection,
      trailing: `#${item.index}`
    }));
    if (target === "agents") return catalog.agents.map((item: AgentCatalogEntry) => ({
      key: item.modelPath, value: item.modelPath, display: item, eyebrow: item.categoryZh || item.category || (item.team === 3 ? "反恐精英" : "恐怖分子"),
      detail: `${item.team === 3 ? "CT" : "T"} · ${item.collectionZh || item.collection || "探员"}`, trailing: `#${item.defIndex}`
    }));
    if (target === "music") return catalog.musicKitItems.map((item: IndexedCatalogEntry) => ({
      key: String(item.index), value: item.index, display: item, eyebrow: "音乐盒", detail: item.collectionZh || item.collection,
      trailing: `#${item.index}`
    }));
    return [];
  };

  const filterForView = (target: View): FilterKey => ({
    gloves: "gloves",
    stickers: "stickers",
    charms: "charms",
    agents: "agents",
    music: "musicKits"
  } as Partial<Record<View, FilterKey>>)[target]!;

  return (
    <div className="app">
      <WindowTitleBar dirty={dirty} />
      <div className="workspace-shell">
        <aside className="sidebar glass">
          <div className="sidebar__source">
            <span className="status-light" />
            <div><strong>本地配置已连接</strong><small title={workspace.pluginDirectory}>{workspace.pluginDirectory}</small></div>
          </div>
          <nav>
            {(Object.keys(VIEW_LABELS) as View[]).map((target) => (
              <button key={target} className={view === target ? "is-active" : ""} onClick={() => setView(target)}>
                {target === "overview" ? <span className="nav-monogram">BR</span> : <AssetImage src={imageUrl(catalog, sidebarImage(target))} alt="" />}
                <span>{VIEW_LABELS[target]}</span>
                {target === "knives" && <small>{catalog.knives.length}</small>}
              </button>
            ))}
          </nav>
          <div className="sidebar__footer">
            <span>catalog</span><code>{catalog.source.commit.slice(0, 8)}</code>
          </div>
        </aside>

        <main className="content">
          <header className="content-head glass">
            <div><div className="section-kicker">CONFIGURATOR</div><h1>{VIEW_LABELS[view]}</h1></div>
            <div className="content-actions">
              <button className="quiet-button" onClick={chooseDirectory}>切换目录</button>
              <button className="primary-button" disabled={!dirty || saving} onClick={save}>
                {saving ? "正在校验…" : dirty ? "保存并热加载" : "配置已同步"}
              </button>
            </div>
          </header>

          <div className="content-scroll">
            {view === "overview" && (
              <Overview
                workspace={workspace}
                draft={draft}
                onOption={setOption}
                onWeaponMode={setWeaponMode}
              />
            )}
            {(["secondary", "smg", "heavy", "rifle", "equipment"] as View[]).includes(view) && (
              <WeaponView
                category={view as "secondary" | "smg" | "heavy" | "rifle" | "equipment"}
                catalog={catalog}
                filter={draft.filters.weaponPaints}
                onMode={(mode) => setFilterMode("weaponPaints", mode)}
                onToggle={(value) => toggleFilterValue("weaponPaints", value)}
                onMarkScope={(values, marked) => markScope("weaponPaints", values, marked)}
              />
            )}
            {view === "knives" && (
              <KnifeView
                catalog={catalog}
                typeFilter={draft.filters.knifeTypes}
                paintFilter={draft.filters.knifePaints}
                onTypeMode={(mode) => setFilterMode("knifeTypes", mode)}
                onPaintMode={(mode) => setFilterMode("knifePaints", mode)}
                onTypeToggle={(value) => toggleFilterValue("knifeTypes", value)}
                onPaintToggle={(value) => toggleFilterValue("knifePaints", value)}
                onMarkPaintScope={(values, marked) => markScope("knifePaints", values, marked)}
              />
            )}
            {(["gloves", "stickers", "charms", "agents", "music"] as View[]).includes(view) && (() => {
              const key = filterForView(view);
              return (
                <SimpleAssetView
                  view={view}
                  catalog={catalog}
                  filter={draft.filters[key] as SelectionFilter<unknown>}
                  candidates={simpleCandidates(view)}
                  onMode={(mode) => setFilterMode(key, mode)}
                  onToggle={(value) => toggleFilterValue(key, value)}
                  onMarkScope={(values, marked) => markScope(key, values, marked)}
                />
              );
            })()}
          </div>
        </main>
      </div>
      {message && (
        <button className={`toast is-${message.kind}`} onClick={() => setMessage(null)}>{message.text}</button>
      )}
    </div>
  );
}
