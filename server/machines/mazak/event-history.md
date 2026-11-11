# Mazak schedule provenance

Durable schedule provenance connects an exact native part identity and native process to its Insight
job, process, path and program information. Capture the as-built association before publishing the
native definition, and retain it after deletion and restart. Historical machine and load/unload
events can then use the recorded route without requiring a currently installed row. The existing
native part naming format remains in use; recycled numeric schedule IDs alone do not establish
historical ownership.

Reserve download generations durably before controller writes, using the existing native naming
format. An uncertain write keeps its generation reserved. Later downloads cannot rebind those full
names to different jobs after the live rows disappear.

For jobs whose quantities Insight owns, the first deployment uses a commissioning cleanout. With the
previous build, finish processing old test CSV history and resolve old equipment operations, then
clear the native test definitions before updating. Retain the manufacturing database and committed
event cursor. Provenance begins with definitions constructed after this cutover; live legacy-work
adoption is outside this upgrade path. Use the route and material history actually available when
interpreting later events. Recording provenance does not reset native counts, move material or award
machining credit.

Native CSV processing retains its established behavior: controller acquisition and acknowledgment
remain ordered by the controller owner; recent events settle and load/unload records retain their
existing grouping rules. Translation logs isolated errors and continues. The committed manufacturing
foreign-ID cursor still controls native retention and acknowledgment. There is no separate raw-event
store, receipt cursor or application-state workflow.

Material identities and actual handling outcomes come from manufacturing history and supported
equipment observations. Where known exact-material history exposes a discrepancy, record a scoped
manufacturing-log diagnostic and use ordinary correction/quarantine. Preserve actual pallet,
material and accepted-operation commitments while independently supported work continues. Missing
historical metadata alone does not establish an indefinite generic hold or authorize event replay.

Mazak prevents deleting or clearing definitions while material is in progress. Native rows can
disappear after physical unloading while the valid handling CSV history reaches Insight later. For
selected jobs, recorded pallet occupancy stays visible with a discrepancy until that history
resolves it. Download cleanup preserves the recorded pallet and its known route resources during
this interval. A missing assignment alone neither moves material nor completes its process.

Pallet handling and basket-station completion remain distinct facts. Use the established handling
flow to establish that material left a pallet; the existing basket-station confirmation records
completion of its listed tasks. Ordinary queue edits and a basket confirmation do not themselves
clear outstanding robot execution or invent a pallet unload.

Adding provenance storage preserves existing material IDs, manufacturing records, the event cursor
and operation receipts. The schema's existing version checks govern binary compatibility. Native
rows remain controller resources; manufacturing quantity ownership is described
[separately](quantity-ownership.md).
