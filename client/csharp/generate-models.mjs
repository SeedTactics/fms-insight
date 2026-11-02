import { spawnSync } from "node:child_process";
import { readFileSync, writeFileSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const check = process.argv.slice(2);
if (check.length > 1 || (check.length === 1 && check[0] !== "--check")) {
  throw new Error("Usage: node generate-models.mjs [--check]");
}
const root = fileURLToPath(new URL("../../", import.meta.url));
const expectedVersion = "14.7.1";
const nswagPackage = new URL("../insight/node_modules/nswag/", import.meta.url);
const actualVersion = JSON.parse(
  readFileSync(new URL("package.json", nswagPackage), "utf8"),
).version;
if (actualVersion !== expectedVersion) {
  throw new Error(
    `C# generation expects NSwag ${expectedVersion}, found ${actualVersion}. Restore the locked client dependencies or review the generator upgrade.`,
  );
}
const artifact = JSON.parse(
  readFileSync(new URL("../../server/fms-insight-api.json", import.meta.url), "utf8"),
);
if (!artifact.openapi?.startsWith("3.0.")) {
  throw new Error(
    "C# generation is qualified against OpenAPI 3.0. Qualify the new document version before changing this boundary.",
  );
}
const schemas = artifact.components?.schemas;
if (!schemas) throw new Error("The OpenAPI document has no component schemas.");
// Historical reads only. Status and program content can be retained as raw on-demand responses.
const roots = [
  "LogEntry",
  "HistoricData",
  "RecentHistoricData",
  "MaterialDetails",
  "JobAndDecrementQuantity",
];
const selected = new Set();
function visit(value) {
  if (value === null || typeof value !== "object") return;
  if (typeof value.$ref === "string") {
    const prefix = "#/components/schemas/";
    if (!value.$ref.startsWith(prefix))
      throw new Error(`Unsupported nonlocal schema reference: ${value.$ref}`);
    const name = decodeURIComponent(value.$ref.slice(prefix.length))
      .replaceAll("~1", "/")
      .replaceAll("~0", "~");
    if (!Object.hasOwn(schemas, name)) throw new Error(`Missing OpenAPI schema: ${name}`);
    if (!selected.has(name)) {
      selected.add(name);
      visit(schemas[name]);
    }
  }
  for (const nested of Object.values(value)) visit(nested);
}
for (const name of roots) visit({ $ref: `#/components/schemas/${name}` });
const configuration = JSON.parse(
  readFileSync(new URL("nswag-settings.json", import.meta.url), "utf8"),
);
const output = fileURLToPath(new URL("Models.Generated.cs", import.meta.url));
const temporary = mkdtempSync(join(tmpdir(), "insight-csharp-models-"));
function run(command, args, input) {
  const result = spawnSync(command, args, {
    cwd: root,
    input,
    encoding: "utf8",
    env: { ...process.env, DOTNET_ROLL_FORWARD: "LatestMajor" },
  });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(`${command} failed:\n${result.stdout}\n${result.stderr}`);
  return result.stdout;
}
try {
  const input = join(temporary, "history.openapi.json");
  const generated = join(temporary, "Models.cs");
  // Retain the original schemas verbatim; selecting the dependency closure is not a second schema.
  writeFileSync(
    input,
    JSON.stringify({
      ...artifact,
      paths: {},
      components: {
        schemas: Object.fromEntries([...selected].toSorted().map((name) => [name, schemas[name]])),
      },
    }),
  );
  const argumentsForGenerator = Object.entries({ ...configuration, output: generated }).map(
    ([name, value]) => `/${name[0].toUpperCase()}${name.slice(1)}:${value}`,
  );
  run(process.execPath, [
    fileURLToPath(new URL("bin/nswag.js", nswagPackage)),
    "openapi2csclient",
    `/Input:${input}`,
    ...argumentsForGenerator,
  ]);
  const formatted = run(
    "dotnet",
    ["csharpier", "format", "--include-generated", "--stdin-path", output],
    readFileSync(generated, "utf8"),
  ).replaceAll("\r\n", "\n");
  if (!formatted.includes("public partial record LogEntry")) {
    throw new Error("Generation or formatting did not produce the expected event model.");
  }
  if (check.length !== 0) {
    if (readFileSync(output, "utf8") !== formatted) {
      throw new Error(
        "Generated C# models are stale. Run pnpm --dir client/insight run generate:csharp and commit the result.",
      );
    }
    console.log(
      `C# models are current (${selected.size} OpenAPI types, NSwag ${expectedVersion}).`,
    );
  } else {
    writeFileSync(output, formatted);
    console.log(`Generated ${selected.size} C# OpenAPI types with NSwag ${expectedVersion}.`);
  }
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
