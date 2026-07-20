import { invoke } from "@tauri-apps/api/core";
import type { RandomizerConfig, WorkspacePayload } from "./types";

export const api = {
  loadWorkspace: (pluginDirectory?: string) =>
    invoke<WorkspacePayload | null>("load_workspace", {
      pluginDirectory: pluginDirectory ?? null
    }),
  saveConfig: (pluginDirectory: string, config: RandomizerConfig) =>
    invoke<void>("save_config", { pluginDirectory, config })
};
