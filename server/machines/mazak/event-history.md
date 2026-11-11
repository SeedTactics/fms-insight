# Mazak schedule provenance

Durable schedule provenance connects an exact native part identity and native process to its
Insight job, process, path and program information. Capture that association before Insight removes
or replaces the native definition, and retain it after deletion and restart. Historical machine and
load/unload events can then use the recorded route without requiring a currently installed row.
The existing native part naming format remains in use; recycled numeric schedule IDs alone do not
establish historical ownership.

Observing an existing definition establishes its current association. A matching row cannot prove
the owner of every older event carrying a previously reused name. Use the provenance and material
history actually available, and preserve the distinction between a verified association and an
unsupported historical inference. Recording provenance does not reset native counts, move material
or award machining credit.

Native CSV processing retains its established behavior: controller acquisition and acknowledgment
remain ordered by the controller owner; recent events settle and load/unload records retain their
existing grouping rules. Translation logs isolated errors and continues. The committed
manufacturing foreign-ID cursor still controls native retention and acknowledgment. There is no
separate raw-event store, receipt cursor or application-state workflow.

Material identities and actual handling outcomes come from manufacturing history and supported
equipment observations. Where known exact-material history exposes a discrepancy, record a scoped
manufacturing-log diagnostic and use ordinary correction/quarantine. Preserve actual pallet,
material and accepted-operation commitments while independently supported work continues. Missing
historical metadata alone does not establish an indefinite generic hold or authorize event replay.

Adding provenance storage preserves existing material IDs, manufacturing records, the event cursor
and operation receipts. The schema's existing version checks govern binary compatibility. Native
rows remain controller resources; manufacturing quantity ownership is described
[separately](quantity-ownership.md).
