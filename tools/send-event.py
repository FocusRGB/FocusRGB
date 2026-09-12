#!/usr/bin/env python3
"""Send a test event to SignalRGB's canvas API.
Usage: python send-event.py [sender] [event] [--port 16034]
Example: python send-event.py demo hit
"""
import sys, urllib.parse, urllib.request

def send(sender="demo", event="hit", port=16034):
    q = urllib.parse.urlencode({"sender": sender, "event": event})
    url = f"http://localhost:{port}/canvas/event?{q}"
    req = urllib.request.Request(url, method="POST", data=b"")
    with urllib.request.urlopen(req, timeout=3) as r:
        print(f"POST {url} -> {r.status}")

if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    port = 16034
    if "--port" in sys.argv:
        port = int(sys.argv[sys.argv.index("--port") + 1])
    sender = args[0] if len(args) > 0 else "demo"
    event  = args[1] if len(args) > 1 else "hit"
    try:
        send(sender, event, port)
    except Exception as e:
        print(f"failed: {e}\nIs SignalRGB running with an onCanvasApiEvent effect selected?")
        sys.exit(1)
