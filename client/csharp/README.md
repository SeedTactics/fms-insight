# Insight C# transport models

This client-only library contains immutable records generated from the saved
[`server/fms-insight-api.json`](../../server/fms-insight-api.json) artifact. It has no reference to
MachineFramework, ASP.NET Core or a generator runtime. The server retains its own public types.
Building the library requires only .NET 10; generating it additionally uses Node, the locked NSwag
package already installed for the website, and the repository's CSharpier tool.

From the repository root, restore the existing tools/dependencies and generate or check the output:

```sh
dotnet tool restore
pnpm --dir client/insight install --frozen-lockfile
pnpm --dir client/insight run generate:csharp
pnpm --dir client/insight run check:csharp
```

Generation reads only the saved artifact; it does not build or inspect the server or run its OpenAPI
exporter. The helper selects the local schema-reference closure of historical events, job history,
material details and decrement quantities, preserving the source schemas. NSwag 14.7.1 applies the
stock settings in `nswag-settings.json`. CSharpier formats the output, including its generated-code
header. The freshness check regenerates into a temporary directory and compares without changing
checked-in output. Review generator upgrades with the model diff and wire tests. When configuring a
stdin formatter, including `jj fix`, pass CSharpier's `--include-generated` flag; otherwise it skips
files with generated-code headers.

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
replacing this library's generator. NSwag would remain a C# generation-time dependency after such a
switch. If the website removes its NSwag package, relocate the tool installation used by the helper;
the models and consuming client code do not depend on that installation location. This process is
currently qualified for OpenAPI 3.0 and rejects other versions explicitly. Qualify changes to the
document version or schema representation against the wire tests; built-in document generation can
select a 3.0 export during a migration.

Tests in `server/test/ClientModelsSpec.cs` compare actual server/MVC JSON with these independent
models, including historical action payloads. The test project references both assemblies as an
oracle; that is not a runtime dependency of this library.
