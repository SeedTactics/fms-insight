# Current-status capture and delivery

HTTP status reads and websocket publications carry complete immutable display snapshots. A backend
that supplies `CurrentStatus.Revision` assigns an increasing number while capturing status under its
existing state lock. Both delivery paths share that sequence. Transport preserves the captured
revision; assigning a number during serialization or sending would give delayed old state a newer
identity. Independent HTTP reads may advance the sequence without a broadcast, so gaps are normal.

The sequence is in memory and starts again with a new backend instance. Browsers compare revisions
only within the current websocket session, resetting on reconnect and ignoring late responses from
ended sessions. Within a session, an incoming snapshot must have a higher revision than the latest
accepted snapshot, whether delivered by HTTP or websocket. Revisions order capture, not equipment
freshness, manufacturing events, or physical work occurrences.

Revision is optional for compatibility with older servers and custom backends. Without comparable
revisions, a websocket snapshot received during an HTTP read takes precedence; intervening HTTP or
local edits cause another read. With revisions, a newer HTTP snapshot can replace an intervening
websocket snapshot directly. Log-derived and optimistic edits still need protection because their
changes are not represented by the last complete snapshot's revision.

A backend that suppresses unchanged display state excludes Revision from its display comparison.
Reading status never consumes a broadcast owed to other clients. Historical log catch-up retains its
separate event-counter cursor, buffering, and deduplication rules.
