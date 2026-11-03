# API generation

From the repository root, install the locked tooling and restore CSharpier:

```sh
pnpm install --frozen-lockfile
dotnet tool restore
```

Use Node 26 or later to run the TypeScript entry point directly:

```sh
node scripts/generate-api.ts
node scripts/generate-api.ts --check
```

The pipeline builds the debug server and exports `server/fms-insight-api.json`, generates the
website's `client/insight/src/network/api.ts` and applies its UTC date fix, then generates immutable
C# transport records in `client/csharp/Models.Generated.cs`. Each output uses the repository's
formatter. NSwag is pinned in the root manifest; its C# settings remain beside the generated models.

All generation takes place in a temporary directory before writing outputs. `--check` compares all
three files and leaves checked-in outputs unchanged; the server build still produces ignored build
artifacts. Run the command without `--check` to update stale files. Changing the schema exporter or
website generator later does not require replacing the C# generation stage, which consumes the
OpenAPI document.

The root package owns shared tooling; `client/insight` owns website dependencies. One root
`pnpm-lock.yaml` covers both packages. Root `pnpm run type-check` checks these scripts, and root
`pnpm run lint` checks the TypeScript throughout the repository. Website build and test commands
remain in `client/insight`.

If this checkout is nested inside another pnpm workspace, exclude this directory and its members
from the enclosing workspace's `packages` patterns. Keep `sharedWorkspaceLockfile` at its default:
it shares this repository's lockfile between root tooling and the website. Setting it to `false`
creates separate member lockfiles; it does not isolate nested workspaces.
