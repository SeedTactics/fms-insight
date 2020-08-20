#!/usr/bin/python

import asyncio
import websockets
import json

uri = "ws://localhost:5000/api/v1/events"

async def consume():
  async with websockets.connect(uri) as websocket:
    async for message in websocket:
      j = json.loads(message)
      if "LogEntry" in j:
        print(json.dumps(j["LogEntry"], indent=2))
        print("")
        print("  -------------------------------------   ")
        print("")

asyncio.get_event_loop().run_until_complete(consume())