import { spawnSync } from "node:child_process";
import { existsSync, readFileSync, writeFileSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

type Json = null | boolean | number | string | ReadonlyArray<Json> | JsonObject;
type JsonObject = { readonly [name: string]: Json };

const root = fileURLToPath(new URL("../", import.meta.url));
const openApiOutput = join(root, "server/fms-insight-api.json");
const websiteOutput = join(root, "client/insight/src/network/api.ts");
const csharpOutput = join(root, "client/csharp/Models.Generated.cs");

function isObject(value: unknown): value is JsonObject {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function object(value: unknown, description: string): JsonObject {
  if (!isObject(value)) throw new Error(`Expected ${description}.`);
  return value;
}

function readObject(path: string): JsonObject {
  const value: unknown = JSON.parse(readFileSync(path, "utf8"));
  return object(value, `JSON object in ${path}`);
}

function packageBin(name: string): string {
  const directory = join(root, "node_modules", name);
  const metadata = readObject(join(directory, "package.json"));
  const bin = object(metadata.bin, `${name} binary mapping`)[name];
  if (typeof bin !== "string") throw new Error(`Missing ${name} executable.`);
  return join(directory, bin);
}

function run(command: string, args: ReadonlyArray<string>, input?: string): string {
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

function nswag(command: string, args: ReadonlyArray<string>): void {
  run(process.execPath, [packageBin("nswag"), command, ...args]);
}

function format(path: string, contents: string): string {
  return run(process.execPath, [packageBin("oxfmt"), "--stdin-filepath", path], contents);
}

function normalizeWebsiteDates(api: string): string {
  const start = api.indexOf("function formatDate(d: Date) {");
  const end = api.indexOf("\n}", start) + 2;
  if (start < 0 || end < 2)
    throw new Error("NSwag output does not contain the expected formatDate function.");
  // NSwag's DateOnly reader uses UTC; its writer must use UTC getters too.
  const replacements: ReadonlyArray<readonly [string, string]> = [
    [".getFullYear()", ".getUTCFullYear()"],
    [".getMonth()", ".getUTCMonth()"],
    [".getDate()", ".getUTCDate()"],
  ];
  const functionBody = replacements.reduce(
    (output, [local, utc]) => {
      if (!output.includes(local) && !output.includes(utc))
        throw new Error("NSwag formatDate no longer contains the expected date getters.");
      return output.replaceAll(local, utc);
    },
    api.slice(start, end),
  );
  return api.slice(0, start) + functionBody + api.slice(end);
}

function references(value: Json): ReadonlyArray<string> {
  if (value === null || typeof value !== "object") return [];
  if (!isObject(value)) return value.flatMap(references);
  const ref = value.$ref;
  if (typeof ref === "string") {
    const prefix = "#/components/schemas/";
    if (!ref.startsWith(prefix)) throw new Error(`Unsupported nonlocal schema reference: ${ref}`);
    return [
      decodeURIComponent(ref.slice(prefix.length)).replaceAll("~1", "/").replaceAll("~0", "~"),
      ...Object.values(value).flatMap(references),
    ];
  }
  return Object.values(value).flatMap(references);
}

function historicalSchemas(schemas: JsonObject): ReadonlyArray<string> {
  const roots: ReadonlyArray<string> = [
    "LogEntry",
    "HistoricData",
    "RecentHistoricData",
    "MaterialDetails",
    "JobAndDecrementQuantity",
  ];
  function closure(names: ReadonlyArray<string>): ReadonlyArray<string> {
    const dependencies = names.flatMap((name) => {
      const schema = schemas[name];
      if (schema === undefined) throw new Error(`Missing OpenAPI schema: ${name}`);
      return references(schema);
    });
    const expanded: ReadonlyArray<string> = [...new Set([...names, ...dependencies])];
    return expanded.length === names.length ? expanded.toSorted() : closure(expanded);
  }
  return closure(roots);
}

function generateCsharp(document: JsonObject, temporary: string): string {
  if (typeof document.openapi !== "string" || !document.openapi.startsWith("3.0."))
    throw new Error(
      "C# generation is qualified against OpenAPI 3.0. Qualify document changes with the wire tests.",
    );
  const schemas = object(
    object(document.components, "OpenAPI components").schemas,
    "OpenAPI schemas",
  );
  const selected = historicalSchemas(schemas);
  const input = join(temporary, "history.openapi.json");
  const output = join(temporary, "Models.cs");
  // Preserve the original schemas; selecting their closure does not create another contract.
  writeFileSync(
    input,
    JSON.stringify({
      ...document,
      paths: {},
      components: { schemas: Object.fromEntries(selected.map((name) => [name, schemas[name]])) },
    }),
  );
  const configuration = readObject(join(root, "client/csharp/nswag-settings.json"));
  const args = Object.entries({ ...configuration, output }).map(([name, value]) => {
    if (typeof value !== "string" && typeof value !== "boolean" && typeof value !== "number")
      throw new Error(`Unsupported NSwag setting: ${name}`);
    return `/${name.charAt(0).toUpperCase()}${name.slice(1)}:${value}`;
  });
  nswag("openapi2csclient", [`/Input:${input}`, ...args]);
  const formatted = run(
    "dotnet",
    ["csharpier", "format", "--include-generated", "--stdin-path", csharpOutput],
    readFileSync(output, "utf8"),
  ).replaceAll("\r\n", "\n");
  if (!formatted.includes("public partial record LogEntry"))
    throw new Error("Generation or formatting did not produce the expected event model.");
  return formatted;
}

function generate(check: boolean): void {
  const expected = object(
    readObject(join(root, "package.json")).devDependencies,
    "root development dependencies",
  ).nswag;
  const actual = readObject(join(root, "node_modules/nswag/package.json")).version;
  if (typeof expected !== "string" || typeof actual !== "string")
    throw new Error("NSwag package metadata must specify a version string.");
  if (expected !== actual)
    throw new Error(
      `NSwag version mismatch: expected ${expected}, found ${actual}. Restore the locked root dependencies.`,
    );
  const temporary = mkdtempSync(join(tmpdir(), "insight-api-"));
  try {
    const openapi = join(temporary, "api.json");
    const website = join(temporary, "api.ts");
    const project = join(root, "server/debug-mock");
    console.log("Generating OpenAPI, website types and C# models...");
    run("dotnet", ["build", project]);
    nswag("aspnetcore2openapi", [`/Project:${project}`, `/Output:${openapi}`, "/NoBuild:true"]);
    nswag("openapi2tsclient", ["/UseAbortSignal:true", `/Input:${openapi}`, `/Output:${website}`]);
    const outputs: ReadonlyArray<{ readonly path: string; readonly contents: string }> = [
      { path: openApiOutput, contents: format(openApiOutput, readFileSync(openapi, "utf8")) },
      {
        path: websiteOutput,
        contents: format(websiteOutput, normalizeWebsiteDates(readFileSync(website, "utf8"))),
      },
      { path: csharpOutput, contents: generateCsharp(readObject(openapi), temporary) },
    ];
    const changed = outputs.filter(
      (output) => !existsSync(output.path) || readFileSync(output.path, "utf8") !== output.contents,
    );
    if (check && changed.length > 0)
      throw new Error(
        `Stale generated files:\n${changed.map((output) => relative(root, output.path)).join("\n")}\nRun node scripts/generate-api.ts and commit the results.`,
      );
    if (!check) for (const output of changed) writeFileSync(output.path, output.contents);
    console.log(
      check
        ? "All generated API files are current."
        : `Updated ${changed.length} generated API files.`,
    );
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
}

try {
  const args: ReadonlyArray<string> = process.argv.slice(2);
  if (args.length > 1 || (args.length === 1 && args[0] !== "--check"))
    throw new Error("Usage: node scripts/generate-api.ts [--check]");
  generate(args.length === 1);
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
}
