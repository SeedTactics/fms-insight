Scope: all 12 workspace projects
Progress: resolved 1, reused 0, downloaded 0, added 0

   ╭──────────────────────────────────────────╮
   │                                          │
   │   Update available! 11.3.0 → 11.18.0.    │
   │   Changelog: https://pnpm.io/v/11.18.0   │
   │     To update, run: pnpm add -g pnpm     │
   │                                          │
   ╰──────────────────────────────────────────╯

Progress: resolved 96, reused 96, downloaded 0, added 0
Progress: resolved 259, reused 196, downloaded 0, added 0
Progress: resolved 924, reused 714, downloaded 0, added 0
[WARN] 7 deprecated subdependencies found: @esbuild-kit/core-utils@3.3.2, @esbuild-kit/esm-loader@2.6.5, boolean@3.2.0, glob@7.2.3, inflight@1.0.6, lodash.isequal@4.5.0, rimraf@2.6.3
Progress: resolved 1306, reused 1057, downloaded 0, added 0
Packages: +2 -1
++-
Progress: resolved 1306, reused 1057, downloaded 0, added 2, done
gui/shell/main postinstall$ electron-builder install-app-deps
gui/shell/main postinstall:   • electron-builder  version=26.15.3
gui/shell/main postinstall:   • loaded configuration  file=package.json ("build" field)
gui/shell/main postinstall:   • packageManager not detected by file, falling back to environment detection  resolvedPackageManager=pnpm detected=/home/wuzzeb/projects/bms/orderlink.alpha/gui/shell/main
gui/shell/main postinstall:   • detected workspace root for project using lock file  pm=pnpm config=undefined resolved=/home/wuzzeb/projects/bms/orderlink.alpha projectDir=/home/wuzzeb/projects/bms/orderlink.alpha/gui/shell/main
gui/shell/main postinstall:   • executing @electron/rebuild  electronVersion=42.7.1 arch=x64 buildFromSource=false workspaceRoot=/home/wuzzeb/projects/bms/orderlink.alpha projectDir=./ appDir=./
gui/shell/main postinstall:   • installing native dependencies  arch=x64
gui/shell/main postinstall:   • completed installing native dependencies
gui/shell/main postinstall: Done

Done in 6.9s using pnpm v11.3.0
/**
 * Supported composition API for applications that embed FMS Insight with custom pages and
 * authenticated endpoints.
 */
export { bootstrapInsight } from "./bootstrap.js";
export { customState } from "./cell-status/custom-state.js";
export type { AppProps } from "./components/App.js";
export { defaultChooseModes } from "./components/ChooseMode.js";
export type { ChooseModeItem } from "./components/ChooseMode.js";
export { RouteLocation } from "./components/routes.js";
export { authenticatedFetch } from "./network/backend.js";
