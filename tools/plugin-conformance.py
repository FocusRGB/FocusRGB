#!/usr/bin/env python3
"""Drive a plugin executable through the FocusRGB 1.0 protocol using only Python's standard library."""

import argparse
import json
import queue
import subprocess
import sys
import threading
import time


MAX_LINE_BYTES = 1024 * 1024
SUPPORT = {"supported", "unverified", "unsupported"}


class ProtocolError(RuntimeError):
    pass


class Session:
    def __init__(self, command: list[str], timeout: float):
        self.timeout = timeout
        self.stderr: list[str] = []
        self.messages: queue.Queue[str | None] = queue.Queue()
        self.process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            bufsize=1,
        )
        threading.Thread(target=self._read_stdout, daemon=True).start()
        threading.Thread(target=self._read_stderr, daemon=True).start()

    def _read_stdout(self) -> None:
        assert self.process.stdout is not None
        for line in self.process.stdout:
            self.messages.put(line)
        self.messages.put(None)

    def _read_stderr(self) -> None:
        assert self.process.stderr is not None
        self.stderr.extend(line.rstrip() for line in self.process.stderr)

    def call(self, request_id: int, method: str, parameters: dict) -> dict:
        self._write({"jsonrpc": "2.0", "id": request_id, "method": method, "params": parameters})
        deadline = time.monotonic() + self.timeout
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise ProtocolError(f"{method} timed out after {self.timeout:g} seconds")
            try:
                line = self.messages.get(timeout=remaining)
            except queue.Empty as exception:
                raise ProtocolError(f"{method} timed out after {self.timeout:g} seconds") from exception
            if line is None:
                raise ProtocolError(f"plugin exited while waiting for {method}")
            if len(line.encode("utf-8")) > MAX_LINE_BYTES + 2:
                raise ProtocolError("plugin wrote a protocol line larger than 1 MiB")
            try:
                message = json.loads(line)
            except json.JSONDecodeError as exception:
                raise ProtocolError(f"plugin stdout was not JSON: {exception}") from exception
            if not isinstance(message, dict) or message.get("jsonrpc") != "2.0":
                raise ProtocolError("plugin returned an invalid JSON-RPC message")
            if "method" in message and "id" not in message:
                if message["method"] != "status/changed" or not isinstance(message.get("params"), dict):
                    raise ProtocolError(f"plugin sent unknown notification {message.get('method')!r}")
                continue
            if message.get("id") != request_id:
                raise ProtocolError(f"plugin returned unexpected response id {message.get('id')!r}")
            if "error" in message:
                raise ProtocolError(f"{method} returned JSON-RPC error: {message['error']}")
            if "result" not in message or not isinstance(message["result"], dict):
                raise ProtocolError(f"{method} returned no object result")
            return message["result"]

    def _write(self, message: dict) -> None:
        assert self.process.stdin is not None
        line = json.dumps(message, separators=(",", ":"))
        if len(line.encode("utf-8")) > MAX_LINE_BYTES:
            raise ProtocolError("conformance request exceeded 1 MiB")
        self.process.stdin.write(line + "\n")
        self.process.stdin.flush()

    def close(self) -> None:
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=1)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=1)


def require_string(value: object, path: str) -> str:
    if not isinstance(value, str) or not value:
        raise ProtocolError(f"{path} must be a non-empty string")
    return value


def require_contract_major(value: object, expected: int, path: str) -> None:
    version = require_string(value, path)
    parts = version.split(".")
    if len(parts) != 2 or not all(part.isdigit() for part in parts) or int(parts[0]) != expected:
        raise ProtocolError(f"{path} must use compatible major {expected}")


def run(plugin_id: str, timeout: float, command: list[str]) -> None:
    session = Session(command, timeout)
    try:
        initialized = session.call(1, "initialize", {
            "contractVersion": "1.0",
            "host": {"name": "FocusRGB Conformance", "version": "1.0.0"},
        })
        if require_string(initialized.get("pluginId"), "initialize.pluginId") != plugin_id:
            raise ProtocolError("initialize.pluginId did not match the expected plugin ID")
        require_contract_major(initialized.get("contractVersion"), 1, "initialize.contractVersion")
        require_string(initialized.get("pluginVersion"), "initialize.pluginVersion")
        capabilities = initialized.get("capabilities")
        if not isinstance(capabilities, list):
            raise ProtocolError("initialize.capabilities must be an array")
        support_by_id = {}
        for capability in capabilities:
            if not isinstance(capability, dict):
                raise ProtocolError("each capability must be an object")
            capability_id = require_string(capability.get("id"), "capability.id")
            support = require_string(capability.get("support"), f"capability {capability_id}.support")
            if support not in SUPPORT:
                raise ProtocolError(f"capability {capability_id} has invalid support {support!r}")
            if capability_id in support_by_id:
                raise ProtocolError(f"capability {capability_id} was reported more than once")
            support_by_id[capability_id] = support

        status = session.call(2, "status/get", {})
        require_string(status.get("availability"), "status.availability")

        catalog = session.call(3, "catalog/list", {"kind": "effect"})
        catalog_support = require_string(catalog.get("support"), "catalog.support")
        if catalog_support not in SUPPORT or not isinstance(catalog.get("items"), list):
            raise ProtocolError("catalog result has invalid support or items")

        next_id = 4
        items = catalog["items"]
        can_apply = support_by_id.get("provider.effect.select") in {"supported", "unverified"}
        if catalog_support == "supported" and items and can_apply:
            item = items[0]
            if not isinstance(item, dict):
                raise ProtocolError("catalog item must be an object")
            effect = {
                "resourceId": require_string(item.get("resourceId"), "catalog item.resourceId"),
                "displayName": require_string(item.get("displayName"), "catalog item.displayName"),
            }
            applied = session.call(next_id, "binding/apply", {
                "profile": "conformance",
                "bindingId": "main",
                "renderingAuthority": "provider",
                "deviceScope": {"kind": "provider-managed"},
                "effect": effect,
            })
            require_string(applied.get("outcome"), "binding/apply.outcome")
            next_id += 1
            released = session.call(next_id, "binding/release", {
                "profile": "conformance",
                "bindingId": "main",
            })
            require_string(released.get("outcome"), "binding/release.outcome")
            next_id += 1

        session.call(next_id, "shutdown", {})
        try:
            exit_code = session.process.wait(timeout=1)
        except subprocess.TimeoutExpired as exception:
            raise ProtocolError("plugin did not exit after shutdown") from exception
        if exit_code != 0:
            raise ProtocolError(f"plugin exited with code {exit_code}")
    finally:
        session.close()


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the FocusRGB plugin protocol 1.0 conformance probe.")
    parser.add_argument("--plugin-id", required=True)
    parser.add_argument("--timeout", type=float, default=5)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    arguments = parser.parse_args()
    command = arguments.command[1:] if arguments.command[:1] == ["--"] else arguments.command
    if not command:
        parser.error("a plugin command is required after --")
    try:
        run(arguments.plugin_id, arguments.timeout, command)
    except (OSError, ProtocolError) as exception:
        print(f"Conformance failed: {exception}", file=sys.stderr)
        return 1
    print(f"Conformance passed for {arguments.plugin_id}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
