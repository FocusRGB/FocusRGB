#!/usr/bin/env python3
"""Send a test event to SignalRGB's Canvas API.
Usage: python send-signalrgb-canvas-event.py [sender] [event] [--port 16034]
Example: python send-signalrgb-canvas-event.py demo hit
"""
import argparse
import sys
import urllib.parse
import urllib.request

def send(sender="demo", event="hit", port=16034):
    q = urllib.parse.urlencode({"sender": sender, "event": event})
    url = f"http://localhost:{port}/canvas/event?{q}"
    req = urllib.request.Request(url, method="POST", data=b"")
    with urllib.request.urlopen(req, timeout=3) as r:
        print(f"POST {url} -> {r.status}")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("sender", nargs="?", default="demo")
    parser.add_argument("event", nargs="?", default="hit")
    parser.add_argument("--port", type=int, default=16034)
    args = parser.parse_args()
    try:
        send(args.sender, args.event, args.port)
    except Exception as e:
        print(f"failed: {e}\nIs SignalRGB running with an onCanvasApiEvent effect selected?")
        return 1
    return 0

if __name__ == "__main__":
    sys.exit(main())
