# Native event receipt and manufacturing application

Mazak CSV receipt and manufacturing application are separate durable facts. Native history can be
acknowledged once its complete payload is saved in the repository, including events awaiting
settling, identity, or correction. A later manufacturing foreign ID never advances an established
receipt cursor. The previous manufacturing cursor is used only to bootstrap an installation with no
inbox. The current stream has the same single-controller-per-repository boundary as the original
`LG` cursor.

Load/unload records remain grouped in their native order. A trailing group stays open until later
evidence seals it; acknowledging its files does not make it applicable. Sealed groups retain their
boundaries across restart, even when intervening independent events have already applied.

Pending groups reserve overlapping pallets, native parts, known jobs/material, and queues involved
in selection or transfer. Unknown dependencies require broader protection. Independent groups can
progress; dependent groups wait in order. Missing-material comparison must not treat a pallet with
unapplied evidence as empty. Download, cleanup, and ordinary queue reconciliation preserve the
resources and commitments of pending work.

Before translation begins, the repository records `Applying`. Successful application and its
callbacks then record `Applied`. Translation currently contains several transactions: identity
selection, manufacturing records, inspections, and notifications can fail at different boundaries.
An interrupted or failed application therefore requires reconciliation of its actual effects; it is
never automatically replayed merely because the application receipt is missing. Read-only deferrals
remain `Received` and retry independently of the native cursor. There is deliberately no blind reset
from uncertain application to retryable receipt.

Received events retain their observed route definitions and ownership. A changed or newly matching
row does not authorize interpreting an older selected event. These observations guard retries; they
do not establish provenance for every legacy event predating the observation. Historical adoption
and terminal correction must establish their own evidence before releasing such work. The native
rows remain resources, not manufacturing authority; see [quantity ownership](quantity-ownership.md).

Schema 47 adds the inbox without changing existing material IDs, event records, or operation
receipts. Older binaries reject the newer schema. Backing up the installation before upgrade
preserves a recovery option; restoring stale databases while equipment has continued working is not
a supported downgrade. Raw received evidence is retained; this change introduces no evidence-expiry
policy.
