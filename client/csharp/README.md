# Insight C# transport models

This client-only library contains immutable records generated from the saved
[`server/fms-insight-api.json`](../../server/fms-insight-api.json) artifact. It has no reference to
MachineFramework, ASP.NET Core or a generator runtime. The server retains its own public types.
Building the library requires only .NET 11. Generation uses Node 26+, the root workspace's locked
NSwag package, and the repository's CSharpier tool.

From the repository root, restore the existing tools/dependencies and generate or check the output:

```sh
dotnet tool restore
pnpm install --frozen-lockfile
node scripts/generate-api.ts
node scripts/generate-api.ts --check
```

The [generation entry point](../../scripts/README.md) exports OpenAPI and regenerates both clients.
Its C# stage consumes that document and selects the local schema-reference closure of historical
events, job history, material details and decrement quantities, preserving the source schemas. NSwag
14.7.1 applies the stock settings in `nswag-settings.json`. CSharpier formats the output, including
its generated-code header. The freshness check regenerates into a temporary directory and compares
without changing checked-in output. Review generator upgrades with the model diff and wire tests.
When configuring a stdin formatter, including `jj fix`, pass CSharpier's `--include-generated` flag;
otherwise it skips files with generated-code headers.

Use `InsightJson.CreateOptions()` to read/write the Insight wire representation. It includes ISO and
legacy .NET duration support, UTC/date-only type mappings, nullable annotations and the web reader
defaults used by Insight. Generated C# property names follow the schema's wire names and can differ
from server-side C# names. String enum values are the wire contract; their generated integer
ordinals are not Insight's internal event codes.

These are transport records. Comments, holds, bookings, archive and controller-delivery state remain
live even on historical job responses. Keep original response JSON separately when retrieval
provenance or unknown fields matter. Typed reserialization does not preserve unknown fields. Current
status and program content can be saved as raw on-demand observations without expanding this
historical model set.

The schema producer, website TypeScript generator and C# generator are independent stages. A future
switch to built-in ASP.NET Core document generation or another website tool does not require
replacing this library's generator. NSwag remains a C# generation-time dependency in the root
workspace after such a switch; generated models and consuming code have no runtime dependency on it.
This process is currently qualified for OpenAPI 3.0 and rejects other versions explicitly. Qualify
changes to the document version or schema representation against the wire tests; built-in document
generation can select a 3.0 export during a migration.

Tests in `server/test/ClientModelsSpec.cs` compare actual server/MVC JSON with these independent
models, including historical action payloads. The test project references both assemblies as an
oracle; that is not a runtime dependency of this library.
