use serde::{de::DeserializeOwned, Deserialize, Serialize};
use std::{
    collections::HashSet,
    fmt::Debug,
    fs,
    hash::Hash,
    path::{Path, PathBuf},
};
use tauri::Manager;

const CONFIG_FILE: &str = "randomizer_config.json";
const CATALOG_FILE: &str = "cosmetic_catalog.json";

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
enum FilterMode {
    Allow,
    Deny,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct SelectionFilter<T> {
    mode: FilterMode,
    items: Vec<T>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "camelCase")]
struct VariantKey {
    def_index: u16,
    paint_kit: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RandomizerOptions {
    enabled: bool,
    #[serde(default)]
    weapon_mode: WeaponRandomizationMode,
    weapons: bool,
    knives: bool,
    gloves: bool,
    agents: bool,
    music: bool,
    stickers: bool,
    charms: bool,
}

#[derive(Debug, Clone, Copy, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
enum WeaponRandomizationMode {
    #[default]
    Persistent,
    Kaleidoscope,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RandomizerFilters {
    knife_types: SelectionFilter<u16>,
    weapon_paints: SelectionFilter<VariantKey>,
    knife_paints: SelectionFilter<VariantKey>,
    gloves: SelectionFilter<VariantKey>,
    stickers: SelectionFilter<u32>,
    charms: SelectionFilter<u32>,
    agents: SelectionFilter<String>,
    music_kits: SelectionFilter<i32>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RandomizerConfig {
    schema_version: u32,
    options: RandomizerOptions,
    filters: RandomizerFilters,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct DisplayFields {
    #[serde(default)]
    id: u32,
    #[serde(default)]
    name: String,
    #[serde(default)]
    name_zh: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    category: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    category_zh: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    collection: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    collection_zh: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    image: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    rarity: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct PaintCatalogEntry {
    paint_kit: i32,
    legacy: bool,
    wear_min: f32,
    wear_max: f32,
    #[serde(flatten)]
    display: DisplayFields,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct WeaponCatalogEntry {
    designer_name: String,
    def_index: u16,
    #[serde(default)]
    team: Option<u8>,
    sticker_schema_count: u32,
    legacy_sticker_schema_count: u32,
    #[serde(flatten)]
    display: DisplayFields,
    paints: Vec<PaintCatalogEntry>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct KnifeCatalogEntry {
    designer_name: String,
    def_index: u16,
    #[serde(flatten)]
    display: DisplayFields,
    paints: Vec<PaintCatalogEntry>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct GloveCatalogEntry {
    def_index: u16,
    paint_kit: i32,
    wear_min: f32,
    wear_max: f32,
    #[serde(flatten)]
    display: DisplayFields,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct IndexedCatalogEntry {
    index: u32,
    #[serde(flatten)]
    display: DisplayFields,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct AgentCatalogEntry {
    def_index: u16,
    team: u8,
    model_path: String,
    #[serde(flatten)]
    display: DisplayFields,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct CatalogSource {
    repository: String,
    commit: String,
    assets_base_url: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct CosmeticCatalog {
    schema_version: u32,
    source: CatalogSource,
    weapons: Vec<WeaponCatalogEntry>,
    knives: Vec<KnifeCatalogEntry>,
    gloves: Vec<GloveCatalogEntry>,
    sticker_kits: Vec<u32>,
    sticker_items: Vec<IndexedCatalogEntry>,
    keychain_definitions: Vec<u32>,
    keychain_items: Vec<IndexedCatalogEntry>,
    music_kits: Vec<i32>,
    music_kit_items: Vec<IndexedCatalogEntry>,
    agents: Vec<AgentCatalogEntry>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct WorkspacePayload {
    plugin_directory: String,
    config_path: String,
    config: RandomizerConfig,
    catalog: CosmeticCatalog,
}

#[derive(Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct PanelState {
    last_plugin_directory: Option<String>,
}

fn read_json<T: DeserializeOwned>(path: &Path) -> Result<T, String> {
    let bytes = fs::read(path).map_err(|error| format!("无法读取 {}：{error}", path.display()))?;
    serde_json::from_slice(&bytes)
        .map_err(|error| format!("{} 不是有效 JSON：{error}", path.display()))
}

fn validate_known<T>(items: &[T], available: &HashSet<T>, label: &str) -> Result<(), String>
where
    T: Clone + Debug + Eq + Hash,
{
    let mut seen = HashSet::new();
    for item in items {
        if !seen.insert(item.clone()) {
            return Err(format!("{label}过滤器存在重复项：{item:?}"));
        }
        if !available.contains(item) {
            return Err(format!(
                "{label}过滤器包含 catalog 中不存在的项目：{item:?}"
            ));
        }
    }
    Ok(())
}

fn includes<T>(filter: &SelectionFilter<T>, value: &T) -> bool
where
    T: PartialEq,
{
    let selected = filter.items.contains(value);
    match filter.mode {
        FilterMode::Allow => selected,
        FilterMode::Deny => !selected,
    }
}

fn validate_catalog(catalog: &CosmeticCatalog) -> Result<(), String> {
    if catalog.schema_version != 2 {
        return Err(format!(
            "不支持的 cosmetic catalog schema：{}",
            catalog.schema_version
        ));
    }
    if catalog.source.repository != "ianlucas/cs2-lib" || catalog.source.commit.len() != 40 {
        return Err("cosmetic catalog 的 Ian Lucas 真源信息无效。".into());
    }
    if catalog.weapons.is_empty()
        || catalog.knives.len() != 20
        || catalog.gloves.is_empty()
        || catalog.sticker_items.is_empty()
        || catalog.keychain_items.is_empty()
        || catalog.music_kit_items.is_empty()
        || catalog.agents.is_empty()
    {
        return Err("cosmetic catalog 缺少 GUI 所需的完整饰品家族。".into());
    }
    Ok(())
}

fn validate_config(config: &RandomizerConfig, catalog: &CosmeticCatalog) -> Result<(), String> {
    if config.schema_version != 1 {
        return Err(format!(
            "不支持的 randomizer config schema：{}",
            config.schema_version
        ));
    }

    let knife_types: HashSet<u16> = catalog.knives.iter().map(|knife| knife.def_index).collect();
    let weapon_paints: HashSet<VariantKey> = catalog
        .weapons
        .iter()
        .flat_map(|weapon| {
            weapon.paints.iter().map(|paint| VariantKey {
                def_index: weapon.def_index,
                paint_kit: paint.paint_kit,
            })
        })
        .collect();
    let knife_paints: HashSet<VariantKey> = catalog
        .knives
        .iter()
        .flat_map(|knife| {
            knife.paints.iter().map(|paint| VariantKey {
                def_index: knife.def_index,
                paint_kit: paint.paint_kit,
            })
        })
        .collect();
    let gloves: HashSet<VariantKey> = catalog
        .gloves
        .iter()
        .map(|glove| VariantKey {
            def_index: glove.def_index,
            paint_kit: glove.paint_kit,
        })
        .collect();
    let agents: HashSet<String> = catalog
        .agents
        .iter()
        .map(|agent| agent.model_path.clone())
        .collect();

    validate_known(&config.filters.knife_types.items, &knife_types, "刀型")?;
    validate_known(
        &config.filters.weapon_paints.items,
        &weapon_paints,
        "武器涂装",
    )?;
    validate_known(
        &config.filters.knife_paints.items,
        &knife_paints,
        "刀具涂装",
    )?;
    validate_known(&config.filters.gloves.items, &gloves, "手套")?;
    validate_known(
        &config.filters.stickers.items,
        &catalog.sticker_kits.iter().copied().collect(),
        "印花",
    )?;
    validate_known(
        &config.filters.charms.items,
        &catalog.keychain_definitions.iter().copied().collect(),
        "挂件",
    )?;
    validate_known(&config.filters.agents.items, &agents, "探员")?;
    validate_known(
        &config.filters.music_kits.items,
        &catalog.music_kits.iter().copied().collect(),
        "音乐盒",
    )?;

    let eligible_knives = catalog.knives.iter().filter(|knife| {
        includes(&config.filters.knife_types, &knife.def_index)
            && knife.paints.iter().any(|paint| {
                includes(
                    &config.filters.knife_paints,
                    &VariantKey {
                        def_index: knife.def_index,
                        paint_kit: paint.paint_kit,
                    },
                )
            })
    });
    if eligible_knives.count() == 0 {
        return Err("当前刀型与刀具涂装过滤组合会让随机池为空。".into());
    }
    if !catalog.gloves.iter().any(|glove| {
        includes(
            &config.filters.gloves,
            &VariantKey {
                def_index: glove.def_index,
                paint_kit: glove.paint_kit,
            },
        )
    }) {
        return Err("当前手套过滤会让随机池为空。".into());
    }
    if !catalog
        .music_kits
        .iter()
        .any(|kit| includes(&config.filters.music_kits, kit))
    {
        return Err("当前音乐盒过滤会让随机池为空。".into());
    }
    for team in [2_u8, 3_u8] {
        if !catalog
            .agents
            .iter()
            .filter(|agent| agent.team == team)
            .any(|agent| includes(&config.filters.agents, &agent.model_path))
        {
            return Err(format!("当前探员过滤会让队伍 {team} 的随机池为空。"));
        }
    }
    Ok(())
}

fn load_workspace_from(directory: &Path) -> Result<WorkspacePayload, String> {
    if !directory.is_dir() {
        return Err(format!("所选路径不是目录：{}", directory.display()));
    }
    let catalog_path = directory.join(CATALOG_FILE);
    let config_path = directory.join(CONFIG_FILE);
    if !catalog_path.is_file() || !config_path.is_file() {
        return Err(format!(
            "{} 中必须同时存在 {} 与 {}。",
            directory.display(),
            CATALOG_FILE,
            CONFIG_FILE
        ));
    }

    let catalog: CosmeticCatalog = read_json(&catalog_path)?;
    validate_catalog(&catalog)?;
    let config: RandomizerConfig = read_json(&config_path)?;
    validate_config(&config, &catalog)?;
    let canonical = directory
        .canonicalize()
        .map_err(|error| format!("无法解析插件目录：{error}"))?;
    Ok(WorkspacePayload {
        plugin_directory: canonical.display().to_string(),
        config_path: config_path.display().to_string(),
        config,
        catalog,
    })
}

fn state_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    let directory = app
        .path()
        .app_config_dir()
        .map_err(|error| format!("无法定位 Panel 配置目录：{error}"))?;
    fs::create_dir_all(&directory).map_err(|error| format!("无法创建 Panel 配置目录：{error}"))?;
    Ok(directory.join("panel-state.json"))
}

fn remember_directory(app: &tauri::AppHandle, directory: &str) -> Result<(), String> {
    let state = PanelState {
        last_plugin_directory: Some(directory.to_owned()),
    };
    let json = serde_json::to_vec_pretty(&state)
        .map_err(|error| format!("无法序列化 Panel 状态：{error}"))?;
    fs::write(state_path(app)?, json).map_err(|error| format!("无法保存 Panel 状态：{error}"))
}

#[tauri::command]
fn load_workspace(
    app: tauri::AppHandle,
    plugin_directory: Option<String>,
) -> Result<Option<WorkspacePayload>, String> {
    let explicit = plugin_directory.is_some();
    let directory = match plugin_directory {
        Some(value) => value,
        None => {
            let local = executable_directory()?;
            if has_workspace_files(Path::new(&local)) {
                local
            } else {
                let path = state_path(&app)?;
                if path.is_file() {
                    let state: PanelState = read_json(&path)?;
                    if let Some(value) = state.last_plugin_directory {
                        value
                    } else {
                        return Ok(None);
                    }
                } else {
                    return Ok(None);
                }
            }
        }
    };

    match load_workspace_from(Path::new(&directory)) {
        Ok(workspace) => {
            remember_directory(&app, &workspace.plugin_directory)?;
            Ok(Some(workspace))
        }
        Err(_) if !explicit => Ok(None),
        Err(error) => Err(error),
    }
}

fn has_workspace_files(directory: &Path) -> bool {
    directory.join(CONFIG_FILE).is_file() && directory.join(CATALOG_FILE).is_file()
}

fn executable_directory() -> Result<String, String> {
    let executable =
        std::env::current_exe().map_err(|error| format!("无法定位 Panel 可执行文件：{error}"))?;
    executable
        .parent()
        .map(|directory| directory.display().to_string())
        .ok_or_else(|| "Panel 可执行文件没有父目录。".to_owned())
}

#[tauri::command]
fn save_config(
    app: tauri::AppHandle,
    plugin_directory: String,
    config: RandomizerConfig,
) -> Result<(), String> {
    let directory = Path::new(&plugin_directory);
    let catalog: CosmeticCatalog = read_json(&directory.join(CATALOG_FILE))?;
    validate_catalog(&catalog)?;
    validate_config(&config, &catalog)?;

    let target = directory.join(CONFIG_FILE);
    let backup = directory.join("randomizer_config.json.bak");
    let pending = directory.join("randomizer_config.json.pending");
    let json = serde_json::to_vec_pretty(&config)
        .map_err(|error| format!("无法序列化 randomizer config：{error}"))?;

    if target.is_file() {
        fs::copy(&target, &backup).map_err(|error| format!("无法备份当前配置：{error}"))?;
    }
    fs::write(&pending, json).map_err(|error| format!("无法写入待保存配置：{error}"))?;
    if let Err(error) = fs::copy(&pending, &target) {
        if backup.is_file() {
            let _ = fs::copy(&backup, &target);
        }
        let _ = fs::remove_file(&pending);
        return Err(format!("无法替换 randomizer config：{error}"));
    }
    let _ = fs::remove_file(&pending);
    remember_directory(&app, &plugin_directory)?;
    Ok(())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![load_workspace, save_config])
        .run(tauri::generate_context!())
        .expect("error while running Bot Randomizer Panel");
}
