// Usage: imported by build-replay.mjs, run-cost.mjs and clean.mjs - the Claude Code transcript folders of this repo
import { readdirSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

// Claude Code names the history folder after the path with every non-alphanumeric character as "-".
export const slugOf = (repoRoot) => repoRoot.replace(/[^A-Za-z0-9]/g, "-");

const projectsDir = (configDir) => path.join(configDir ?? (process.env.CLAUDE_CONFIG_DIR || path.join(os.homedir(), ".claude")), "projects");

export function historyDir({ repoRoot = REPO_ROOT, configDir } = {}) {
  return path.join(projectsDir(configDir), slugOf(repoRoot));
}

// The repo folder plus every sibling named `<slug>-...` (worktrees); a folder that merely shares the leading characters is not one.
export function historyDirs({ repoRoot = REPO_ROOT, configDir } = {}) {
  const root = projectsDir(configDir);
  const slug = slugOf(repoRoot);
  let siblings = [];
  try {
    siblings = readdirSync(root, { withFileTypes: true })
      .filter((e) => e.isDirectory() && e.name.startsWith(`${slug}-`))
      .map((e) => path.join(root, e.name))
      .sort();
  } catch {
    siblings = [];
  }
  return [path.join(root, slug), ...siblings];
}

export const HISTORY = historyDir();
