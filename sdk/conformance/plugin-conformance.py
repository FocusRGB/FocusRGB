#!/usr/bin/env python3
"""Drive a plugin executable through the FocusRGB 1.3 protocol using only Python's standard library."""

import argparse
import json
import math
import queue
import re
import subprocess
import sys
import threading
import time


MAX_LINE_BYTES = 1024 * 1024
SUPPORT = {"supported", "unverified", "unsupported"}
RESOURCE_ID = re.compile(r"^[a-z][a-z0-9-]*(?:\.[a-z0-9][a-z0-9-]*)+$")


class ProtocolError(RuntimeError):
    pass


class Session:
    def __init__(self, command: list[str], timeout: float):
        self.timeout = timeout
        self.stderr: list[str] = []
        self.notifications: list[dict] = []
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
                if message["method"] == "status/changed" and isinstance(message.get("params"), dict):
                    continue
                if message["method"] == "game/telemetry" and isinstance(message.get("params"), dict):
                    self.notifications.append(message)
                    continue
                else:
                    raise ProtocolError(f"plugin sent unknown notification {message.get('method')!r}")
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


def require_telemetry_value(value: object, path: str) -> None:
    if not isinstance(value, dict):
        raise ProtocolError(f"{path} must be an object")
    resource_id = value.get("resourceId")
    if not isinstance(resource_id, str) or len(resource_id) > 128 or RESOURCE_ID.fullmatch(resource_id) is None:
        raise ProtocolError(f"{path}.resourceId must be a namespaced string")
    present = [name for name in ("boolean", "number", "string") if name in value]
    if len(present) != 1:
        raise ProtocolError(f"{path} must contain exactly one typed value")
    typed = value[present[0]]
    if present[0] == "boolean" and not isinstance(typed, bool):
        raise ProtocolError(f"{path}.boolean must be a boolean")
    if present[0] == "number" and (
            not isinstance(typed, (int, float)) or isinstance(typed, bool) or not math.isfinite(typed)):
        raise ProtocolError(f"{path}.number must be a number")
    if present[0] == "string" and (not isinstance(typed, str) or len(typed) > 1024):
        raise ProtocolError(f"{path}.string must be a bounded string")


def require_telemetry_notification(message: dict) -> int:
    parameters = message.get("params")
    if not isinstance(parameters, dict):
        raise ProtocolError("game/telemetry.params must be an object")
    source = parameters.get("source")
    game = source.get("game") if isinstance(source, dict) else None
    if not isinstance(game, dict):
        raise ProtocolError("game/telemetry.source.game must be an object")
    require_string(game.get("launcher"), "game/telemetry.source.game.launcher")
    require_string(game.get("id"), "game/telemetry.source.game.id")
    require_string(source.get("adapter"), "game/telemetry.source.adapter")
    sequence = parameters.get("sequence")
    fresh = parameters.get("freshForMs")
    disconnected = parameters.get("disconnectAfterMs")
    if not isinstance(sequence, int) or sequence < 0:
        raise ProtocolError("game/telemetry.sequence must be a non-negative integer")
    if (not isinstance(fresh, int) or not 100 <= fresh <= 60000
            or not isinstance(disconnected, int) or not 101 <= disconnected <= 300000
            or disconnected <= fresh):
        raise ProtocolError("game/telemetry freshness durations are invalid")
    require_string(parameters.get("observedAt"), "game/telemetry.observedAt")
    inputs = parameters.get("inputs")
    events = parameters.get("events")
    if not isinstance(inputs, list) or not isinstance(events, list):
        raise ProtocolError("game/telemetry inputs and events must be arrays")
    if len(inputs) > 2048 or len(events) > 128:
        raise ProtocolError("game/telemetry inputs or events exceeded protocol bounds")
    input_ids = set()
    for index, value in enumerate(inputs):
        require_telemetry_value(value, f"game/telemetry.inputs[{index}]")
        if value["resourceId"] in input_ids:
            raise ProtocolError("game/telemetry input resource IDs must be unique")
        input_ids.add(value["resourceId"])
    for index, event in enumerate(events):
        if not isinstance(event, dict):
            raise ProtocolError(f"game/telemetry.events[{index}] must be an object")
        event_id = require_string(event.get("resourceId"), f"game/telemetry.events[{index}].resourceId")
        if len(event_id) > 128 or RESOURCE_ID.fullmatch(event_id) is None:
            raise ProtocolError(f"game/telemetry.events[{index}].resourceId must be namespaced")
        require_string(event.get("observedAt"), f"game/telemetry.events[{index}].observedAt")
        values = event.get("values")
        if not isinstance(values, list):
            raise ProtocolError(f"game/telemetry.events[{index}].values must be an array")
        if len(values) > 64:
            raise ProtocolError(f"game/telemetry.events[{index}].values exceeded protocol bounds")
        for value_index, value in enumerate(values):
            require_telemetry_value(value, f"game/telemetry.events[{index}].values[{value_index}]")
    return sequence


def run(plugin_id: str, timeout: float, require_telemetry: bool, command: list[str]) -> None:
    session = Session(command, timeout)
    try:
        initialized = session.call(1, "initialize", {
            "contractVersion": "1.3",
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

        sdk = initialized.get("sdk")
        if sdk is not None:
            if not isinstance(sdk, dict) or set(sdk) != {"name", "language", "version"}:
                raise ProtocolError("initialize.sdk must contain only name, language, and version")
            require_string(sdk.get("name"), "initialize.sdk.name")
            require_string(sdk.get("language"), "initialize.sdk.language")
            sdk_version = require_string(sdk.get("version"), "initialize.sdk.version")
            if re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", sdk_version) is None:
                raise ProtocolError("initialize.sdk.version must use major.minor.patch")

        status = session.call(2, "status/get", {})
        require_string(status.get("availability"), "status.availability")

        catalog = session.call(3, "catalog/list", {"kind": "effect"})
        catalog_support = require_string(catalog.get("support"), "catalog.support")
        if catalog_support not in SUPPORT or not isinstance(catalog.get("items"), list):
            raise ProtocolError("catalog result has invalid support or items")

        next_id = 4
        if support_by_id.get("game.catalog") in {"supported", "unverified"}:
            games = session.call(next_id, "catalog/list", {"kind": "game"})
            next_id += 1
            game_support = require_string(games.get("support"), "game catalog.support")
            game_items = games.get("items")
            if game_support not in SUPPORT or not isinstance(game_items, list):
                raise ProtocolError("game catalog result has invalid support or items")
            for index, item in enumerate(game_items):
                if not isinstance(item, dict):
                    raise ProtocolError(f"game catalog item {index} must be an object")
                require_string(item.get("resourceId"), f"game catalog item {index}.resourceId")
                require_string(item.get("displayName"), f"game catalog item {index}.displayName")
                if not isinstance(item.get("available"), bool):
                    raise ProtocolError(f"game catalog item {index}.available must be a boolean")
                game = item.get("game")
                if not isinstance(game, dict) or set(game) != {"executables"}:
                    raise ProtocolError(f"game catalog item {index}.game must contain only executables")
                executables = game.get("executables")
                if not isinstance(executables, list):
                    raise ProtocolError(f"game catalog item {index}.game.executables must be an array")
                checked = []
                for executable in executables:
                    name = require_string(executable, f"game catalog item {index}.game.executables[]")
                    if "/" in name or "\\" in name:
                        raise ProtocolError("game catalog executables must be file names, not paths")
                    checked.append(name)
                if len(checked) != len(set(checked)):
                    raise ProtocolError("game catalog executables must be unique")

        if support_by_id.get("game.telemetry") in {"supported", "unverified"}:
            telemetry = session.call(next_id, "catalog/list", {"kind": "telemetry"})
            next_id += 1
            if telemetry.get("support") not in SUPPORT or not isinstance(telemetry.get("items"), list):
                raise ProtocolError("telemetry catalog result has invalid support or items")
            if len(telemetry["items"]) > 4096:
                raise ProtocolError("telemetry catalog exceeded protocol bounds")
            descriptor_ids = set()
            for index, item in enumerate(telemetry["items"]):
                if not isinstance(item, dict):
                    raise ProtocolError(f"telemetry catalog item {index} must be an object")
                resource_id = require_string(
                    item.get("resourceId"), f"telemetry catalog item {index}.resourceId")
                if len(resource_id) > 128 or RESOURCE_ID.fullmatch(resource_id) is None:
                    raise ProtocolError(f"telemetry catalog item {index}.resourceId must be namespaced")
                require_string(item.get("displayName"), f"telemetry catalog item {index}.displayName")
                if not isinstance(item.get("available"), bool):
                    raise ProtocolError(f"telemetry catalog item {index}.available must be a boolean")
                metadata = item.get("telemetry")
                if not isinstance(metadata, dict):
                    raise ProtocolError(f"telemetry catalog item {index}.telemetry must be an object")
                game = metadata.get("game")
                section = metadata.get("section")
                if not isinstance(game, dict) or not isinstance(section, dict):
                    raise ProtocolError(f"telemetry catalog item {index} has invalid game or section")
                require_string(game.get("launcher"), f"telemetry catalog item {index}.game.launcher")
                require_string(game.get("id"), f"telemetry catalog item {index}.game.id")
                identity = (game["launcher"], game["id"], resource_id)
                if identity in descriptor_ids:
                    raise ProtocolError("telemetry catalog identities must be unique")
                descriptor_ids.add(identity)
                require_string(section.get("id"), f"telemetry catalog item {index}.section.id")
                require_string(section.get("displayName"), f"telemetry catalog item {index}.section.displayName")
                if not isinstance(section.get("order"), int):
                    raise ProtocolError(f"telemetry catalog item {index}.section.order must be an integer")
                kind = require_string(metadata.get("kind"), f"telemetry catalog item {index}.kind")
                value_type = require_string(
                    metadata.get("valueType"), f"telemetry catalog item {index}.valueType")
                description = require_string(
                    metadata.get("description"), f"telemetry catalog item {index}.description")
                if len(description) > 512:
                    raise ProtocolError(f"telemetry catalog item {index}.description exceeded protocol bounds")
                if kind not in {"input", "event"} or value_type not in {"boolean", "number", "string", "event"}:
                    raise ProtocolError(f"telemetry catalog item {index} has invalid kind or valueType")
                if (kind == "event") != (value_type == "event"):
                    raise ProtocolError(f"telemetry catalog item {index} event kind and valueType disagree")
                if value_type != "number" and ("minimum" in metadata or "maximum" in metadata):
                    raise ProtocolError("numeric hints require a number telemetry descriptor")
                if value_type != "string" and "choices" in metadata:
                    raise ProtocolError("choices require a string telemetry descriptor")
                minimum = metadata.get("minimum")
                maximum = metadata.get("maximum")
                if minimum is not None and (
                        not isinstance(minimum, (int, float)) or isinstance(minimum, bool)
                        or not math.isfinite(minimum)):
                    raise ProtocolError("telemetry minimum must be a finite number")
                if maximum is not None and (
                        not isinstance(maximum, (int, float)) or isinstance(maximum, bool)
                        or not math.isfinite(maximum)):
                    raise ProtocolError("telemetry maximum must be a finite number")
                if minimum is not None and maximum is not None and minimum > maximum:
                    raise ProtocolError("telemetry minimum cannot exceed maximum")
                choices = metadata.get("choices")
                if choices is not None:
                    if not isinstance(choices, list) or len(choices) > 128:
                        raise ProtocolError("telemetry choices must be a bounded array")
                    for choice_index, choice in enumerate(choices):
                        if not isinstance(choice, dict):
                            raise ProtocolError(f"telemetry choice {choice_index} must be an object")
                        require_string(choice.get("value"), f"telemetry choice {choice_index}.value")
                        require_string(choice.get("displayName"), f"telemetry choice {choice_index}.displayName")
            last_sequence = None
            for notification in session.notifications:
                sequence = require_telemetry_notification(notification)
                if last_sequence is not None and sequence <= last_sequence:
                    raise ProtocolError("game/telemetry sequences must increase per plugin process")
                last_sequence = sequence
        elif require_telemetry:
            raise ProtocolError("--require-telemetry needs the game.telemetry capability")

        if session.notifications and support_by_id.get("game.telemetry") not in {"supported", "unverified"}:
            raise ProtocolError("plugin published telemetry without declaring game.telemetry")

        if require_telemetry and not session.notifications:
            raise ProtocolError("plugin did not publish a game/telemetry notification")

        if support_by_id.get("provider.effect.input") in {"supported", "unverified"}:
            effect_input = session.call(next_id, "effect/input", {
                "profile": "conformance",
                "bindingId": "main",
                "mappingId": "synthetic-level",
                "sourcePluginId": plugin_id,
                "source": {
                    "game": {"launcher": "example", "id": "sample-game"},
                    "adapter": "example.synthetic",
                },
                "sourceResourceId": "example.status.level",
                "sequence": 1,
                "receivedAt": "1970-01-01T00:00:00Z",
                "freshness": "fresh",
                "conditionMatched": True,
                "targetInputId": "example.effect.level",
                "value": {"resourceId": "example.status.level", "number": 42},
            })
            next_id += 1
            if effect_input.get("outcome") not in {"accepted", "unsupported", "unavailable"}:
                raise ProtocolError("effect/input returned an invalid outcome")

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
    parser = argparse.ArgumentParser(description="Run the FocusRGB plugin protocol 1.3 conformance probe.")
    parser.add_argument("--plugin-id", required=True)
    parser.add_argument("--timeout", type=float, default=5)
    parser.add_argument("--require-telemetry", action="store_true")
    parser.add_argument("command", nargs=argparse.REMAINDER)
    arguments = parser.parse_args()
    command = arguments.command[1:] if arguments.command[:1] == ["--"] else arguments.command
    if not command:
        parser.error("a plugin command is required after --")
    try:
        run(arguments.plugin_id, arguments.timeout, arguments.require_telemetry, command)
    except (OSError, ProtocolError) as exception:
        print(f"Conformance failed: {exception}", file=sys.stderr)
        return 1
    print(f"Conformance passed for {arguments.plugin_id}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
