# Basket material and station work

Baskets have stable integer identities and one-based physical slots. A slot contains identified
material at a process, with namespaced metadata for the integration's physical grouping. The
repository's current-contents tables project committed manufacturing history; sensing and operator
suggestions do not independently change material ownership. Current contents are compared
structurally, including material/process pairs and metadata, regardless of dictionary or material
iteration order. One material identity cannot occupy two current basket slots.

A manufacturing completion has three parts: exact transfers, basket cycle boundaries, and expected
and resulting contents. A pallet completion requires the corresponding pallet load or unload for
every basket transfer. Ordinary pallet unloads can still omit a queue destination; an explicit
basket transfer identifies which unloaded material enters a basket. Station-only completions have no
pallet counterpart. The repository commits events, contents and its durable operation receipt in one
SQLite transaction, or rejects the whole operation. Queue delivery to an external system is outside
that transaction.

An idempotency key identifies one complete operation. Equivalent retries return the original
receipt; a changed payload conflicts. Fingerprints encode collection boundaries as well as values.
Expected missing contents differ from a known empty basket. Callers must keep the same identity when
retrying after a lost response or restart. Integration-specific saved station work owns its
readiness and completion; the generic `POST /api/v1/jobs/basket-load-station/complete` sends only
its opaque `WorkId` through `IJobAndQueueControl`.

For basket load/unload and cycle events, `LogEntry.Pallet` carries the basket identity. A
basket-side material `Face` is its slot. These conventions retain the existing event API; new
metadata keys must be namespaced. Current basket position and unknown slots are display evidence,
separate from committed material contents and completion permission.

Material operations distinguish physical location from authority. `Free` means no cell-tracked
location, including raw material during loading; it does not establish operator ownership. Human
loading and unloading protect material from cross-queue moves, queue removal and invalidation.
Same-queue priority reordering remains allowed for queued material, including during human loading,
unless `AutomatedTransfer` is declared. A nonblank `LoadCancellationId` grants cancellation of the
backend's complete atomic instruction, subject to current-work validation. Blank tokens remain
protected but grant no cancellation.

`AutomatedTransfer` defaults to false. A backend can set it only when automation controls the
accepted transfer, and must retain quarantine signals through completion, delayed manufacturing
records and restart. Loading signals use the action's target process/path and commanded pallet
context while the physical location still describes the source. Actual disposition followed by fresh
preparation/readmission ends the signal. Declared transfers cannot be cancelled through an operator
load token. Built-in adapters leave this flag false until they implement that retention. Human
unload actions permit deferred signaling; direct queued quarantine is a separate operation.

Backends extend `IJobAndQueueControl` for station completion and cancellation, supply exact Mazak
load/unload resolution where controller evidence cannot determine identities, and can publish
integration-specific guidance through custom status. Extensions use the shared React, React DOM and
Jotai runtimes, composing their page within the appropriate store provider. Custom guidance and
physical sensing remain separate from the repository's manufacturing authority.
