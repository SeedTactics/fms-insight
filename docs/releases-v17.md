# v17 migration from v16.8.3

Deploy rebuilt plugins, matching server packages and the client together. C# interface, constructor
and optional-argument changes require rebuilding plugins; source-compatible optional arguments do
not preserve old CLR method signatures.

## Material operations

Quarantine has two commands: `signal-quarantine` records deferred disposition for material
controlled by automation or being unloaded by an operator, while `quarantine-queued` moves eligible
queued material directly. The operator signal button is labeled **Signal for quarantine** when a
quarantine queue is configured, or **Scrap** otherwise; both record deferred disposition. Human
loading uses explicit `cancel-load`, validated against its current `LoadCancellationId`, before
cross-queue moves, queue removal or cycle invalidation. Same-queue priority reordering remains
allowed during human loading. Automated transfers are declared by the backend's default-false
`AutomatedTransfer` field and remain protected from cancellation and direct edits. Declaring
backends must preserve signals through delayed events and restart. `Free` describes an untracked
location and does not grant operator edit permission.

Cycle invalidation is stepwise. v16.8.3 invalidated the selected and all later processes together;
v17 requires separate actions from the highest completed process downward. Ownership and affected
group guards also apply to queue removal and invalidation. No-queue routes remain supported.

Serial swapping and `PUT /api/v1/jobs/material/{materialId}/swap-off-pallet` are removed. Status
flags `AllowSwapSerialAtLoadStation` and `AllowQuarantineToCancelLoad`, and websocket event
`EditMaterialInLog`, are removed. Historical swap event value 113 remains readable; retired basket
location value 118 is not reused and may serialize numerically.

## Baskets and extensions

Status adds basket slots, work/cancellation identities, `LoadingToBasket`, target information and
custom state. Generic basket station completion uses
`POST /api/v1/jobs/basket-load-station/complete` with a `WorkId` body. Backend completion and retry
remain durable. It replaces the `AppProps` completion callback. Unused movement-arrival components,
exports and `BasketMoveInstructions` contracts are removed.

React, React DOM and Jotai are peer dependencies of the extension package. Hosts must supply
compatible runtimes and the intended Jotai provider/store; merely installing the package does not
connect an unrelated store. Internal exported subpaths do not constitute a stable extension API.

Pallet-building convenience helpers used by private integrations are removed from the public
`BuildCellState` class. Public queue helpers remain. Integrations must provide their own pallet
helpers. Ordinary unload destinations retain their legacy shape, including null queue destinations;
explicit basket transfers determine basket membership. See the
[basket design](../server/lib/BlackMaple.MachineFramework/basket-design.md) for event and
persistence conventions.

## Controllers and deployment

Mazak Version E and its database-kit/log handling are retired. Mazak Web and Smooth retain their
controller numbers, CSV translation and proxy support. `Starting Load Station Number` is removed;
`Starting Pallet Number` remains supported. The web-app manifest is intentionally removed: existing
installed shortcuts may need to be recreated using the normal browser page.

The database upgrade adds basket projection/receipt storage and nullable event correlation identity
while retaining ordinary pallet history and accounting. Legacy foreign IDs and original messages are
stored unchanged. Upgrade qualification must include representative historical database copies,
supported controller replay and Windows proxy installation before production rollout.
