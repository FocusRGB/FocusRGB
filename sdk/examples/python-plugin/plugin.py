#!/usr/bin/env python3
"""Minimal non-.NET FocusRGB plugin example using only Python's standard library."""

import json
import sys


PLUGIN_ID = "python-example"
EFFECT = {"resourceId": "python-static-blue", "displayName": "Static Blue"}


def result(request_id: int, value: dict) -> None:
    print(json.dumps({"jsonrpc": "2.0", "id": request_id, "result": value}, separators=(",", ":")), flush=True)


def error(request_id: object, code: int, message: str) -> None:
    print(json.dumps({
        "jsonrpc": "2.0",
        "id": request_id,
        "error": {"code": code, "message": message},
    }, separators=(",", ":")), flush=True)


def main() -> int:
    current = None
    print("Python example started.", file=sys.stderr, flush=True)
    for line in sys.stdin:
        try:
            request = json.loads(line)
        except json.JSONDecodeError as exception:
            error(None, -32700, str(exception))
            continue
        method = request.get("method")
        if method == "$/cancel":
            continue
        request_id = request.get("id")
        parameters = request.get("params", {})
        if not isinstance(request_id, int) or not isinstance(parameters, dict):
            error(None, -32600, "Invalid JSON-RPC request.")
            continue
        if method == "initialize":
            offered = parameters.get("contractVersion", "")
            if not isinstance(offered, str) or offered.split(".")[0] != "1":
                error(request_id, -32602, "Contract major 1 is required.")
                continue
            result(request_id, {
                "pluginId": PLUGIN_ID,
                "pluginVersion": "1.0.0",
                "contractVersion": "1.0",
                "capabilities": [
                    {"id": "provider.effect.catalog", "support": "supported"},
                    {"id": "provider.effect.select", "support": "supported"},
                    {"id": "provider.layout.catalog", "support": "unsupported",
                     "reason": {"code": "capability-unsupported", "message": "This example has no layouts."}},
                ],
            })
        elif method == "status/get":
            value = {"availability": "available"}
            if current is not None:
                value["current"] = {"effect": current}
            result(request_id, value)
        elif method == "catalog/list":
            if parameters.get("kind") == "effect":
                result(request_id, {"support": "supported", "items": [{**EFFECT, "available": True}]})
            else:
                result(request_id, {
                    "support": "unsupported",
                    "items": [],
                    "reason": {"code": "capability-unsupported", "message": "This example has no layouts."},
                })
        elif method == "binding/apply":
            current = parameters.get("effect")
            result(request_id, {"outcome": "applied", "actual": {"effect": current}})
        elif method == "binding/release":
            current = None
            result(request_id, {"outcome": "released"})
        elif method == "shutdown":
            result(request_id, {})
            return 0
        else:
            error(request_id, -32601, f"Unknown method '{method}'.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
