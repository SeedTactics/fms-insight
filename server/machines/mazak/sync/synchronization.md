# Mazak synchronization ownership

`MazakSync.CalculateCellState` composes the same acquire, reconcile and acknowledge phases used by
staged callers. Integrations that need other equipment and operator work to progress during
controller I/O can instead stage synchronization. Keep one controller owner across acquisition,
reconciliation and acknowledgment, while holding the repository mutation owner only for watermark
capture and reconciliation. Take the controller owner first; never wait for it while holding the
repository mutation owner. Each independent Mazak stream needs its own source-watermark domain; the
existing repository-wide LG cursor does not partition multiple streams automatically.

Acquisition uses a previously captured source watermark and owns no repository connection. It
materializes controller rows, nested process collections and any required tool rows before
returning. Its original start and completion times describe an observation interval, not an atomic
controller snapshot. Machine-event settling uses the time controller rows and logs finished loading,
captured before the tool read. A cycle ending during slow acquisition must still settle before its
tool counters can be used. Reconciliation uses that same captured time: an early tool read cannot
become eligible merely because processing was delayed. A later acquisition supplies the settled
evidence. Legacy wire DTOs remain compatible with the .NET 3.5 proxy. Acquisition makes one eager,
immutable copy of their collections, including nested station-pallet material.

Tool observations are supplementary. A failed tool read is logged and reported as an informational
diagnostic while usable controller data continues through manufacturing reconciliation. The
synchronizer retains the last accepted acquisition's tool-read failure until an actual successful
observation (including an observed empty list); a poll without a tool query does not establish
recovery. This process-local source diagnostic begins unknown after reconstruction, while the
history marker below remains durable. Tool evidence is retained when the ordered observation enters
reconciliation, even when later local status construction fails. A subsequent no-query poll cannot
erase it. Ingestion-diagnostic changes set `StateUpdated` even without manufacturing events, so
timer-driven consumers publish the same assessment served by HTTP. Unobserved tools are distinct
from an observed empty list. Missing measurements cannot establish tool removal or tool use; an
unavailable start snapshot remains identified in cycle history after restart.

Reconciliation uses the current repository and invokes the configured resolvers and notifications
under the caller's mutation owner. It performs no built-in controller I/O. An operator edit or
reconnect does not automatically discard readable history or status. Preserve newer corrections by
resolving against current repository facts and scope any restriction to the particular inference or
suggestion that needs newer evidence. Filter captured events against current durable source progress
before translation, including when retrying after a local status failure; rebuilding status must not
replay manufacturing effects. Callers keep observations in source order and decide which suggestions
require fresh controller context. Existing event-level manufacturing commits remain durable even if
later status construction fails; the scan is not one database transaction. Isolated event-processing
errors are logged and skipped so later evidence and current-status construction continue. Reconcile
the resulting gaps using current facts and the integration's ordinary local correction/quarantine
policy. Source-wide acquisition or repository failure still requires reporting the unavailable
source and retrying; an isolated bad record must not become a permanent ingestion barrier.

The resulting state retains the incomplete load/unload group's pallet in Insight coordinates. When
projecting current repository facts over retained controller tables, preserve that context and the
original controller observation time. Such a projection does not process events, infer missing
pallet material or establish source recovery; health-only publication must not replay
reconciliation.

CSV parsing, file discovery, ordering, opening/retries and deletion retain their established
behavior. An unreadable file may contain valid evidence; it is not a malformed record that may be
skipped. Any change to file contention or retry policy requires separate operational qualification,
including whether acknowledgment can advance past unread evidence. Independent source-health
publication comes from releasing the mutation owner during acquisition, not from shortening CSV
waits.

A successful reconciliation returns the captured durable source watermark with the calculated state.
It may advance past a logged, skipped record under this best-effort ingestion policy. Acknowledge
that captured receipt outside the mutation owner, rather than reading a newer repository watermark.
A delayed acknowledgment therefore cannot delete evidence from later work. An acknowledgment failure
does not undo manufacturing commits: retain the receipt for acknowledgment retry, or reacquire using
the durable source watermark after restart. Do not replay manufacturing effects to retry
acknowledgment.

This separation supplies the ownership boundary. Independent health publication, acquisition
deadlines, shutdown and eligibility of source-dependent suggestions remain the host integration's
responsibility.
