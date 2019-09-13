#!/usr/bin/python3

import datetime
import json
import os
import urllib.request
import urllib.parse

host = "http://localhost:5000"

# Load the last seen event counter from the file names in the current directory
lastSeenCntr = None
for f in os.listdir("."):
    if os.path.isfile(f):
        cntr = int(os.path.splitext(os.path.basename(f))[0])
        if not lastSeenCntr or cntr > lastSeenCntr:
            lastSeenCntr = cntr

if lastSeenCntr:
    # load recent events (if any)
    with urllib.request.urlopen("%s/api/v1/log/events/recent?lastSeenCounter=%i" % (host, lastSeenCntr)) as r:
        events = json.loads(r.read())
else:
    # Load the last 7 days of events
    now = datetime.datetime.utcnow()
    query = urllib.parse.urlencode({
      "startUTC": (now - datetime.timedelta(days=7)).strftime("%Y-%m-%dT%H:%M:%SZ"),
      "endUTC": now.strftime("%Y-%m-%dT%H:%M:%SZ")
    })
    req = urllib.request.Request("%s/api/v1/log/events/all?%s" % (host, query))
    req.add_header("Accept", "application/json")
    with urllib.request.urlopen(req) as r:
        events = json.loads(r.read())

# Write the events as files, one event per file.  The name of the file is the event counter so
# that the lastSeenCounter can easily be computed from the filenames.
for event in events:
    with open(str(event["counter"]) + ".json", "w") as f:
        json.dump(event, f)