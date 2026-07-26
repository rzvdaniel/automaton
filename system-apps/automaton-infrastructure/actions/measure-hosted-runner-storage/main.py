#!/usr/bin/env python3
import json
import os
import posixpath
import subprocess
import sys
import uuid


def host(*arguments: str, timeout: int = 90) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["automaton-host-exec", *arguments],
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=timeout,
    )


def resolve_docker_root() -> str:
    result = host("docker", "info", "--format", "{{.DockerRootDir}}")
    root = result.stdout.strip()
    if result.returncode != 0 or not root.startswith("/") or "\x00" in root:
        detail = result.stderr.strip()
        raise RuntimeError(f"Could not resolve the host Docker data root{': ' + detail if detail else ''}")
    return root.rstrip("/")


def validate_and_resolve(volume_name: str, runner_id: str, scope: str, docker_root: str) -> str:
    result = host("docker", "volume", "inspect", volume_name)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or f"Could not inspect Docker volume {volume_name}")
    try:
        metadata = json.loads(result.stdout)[0]
        labels = metadata["Labels"] or {}
        mountpoint = metadata["Mountpoint"]
    except (json.JSONDecodeError, IndexError, KeyError, TypeError) as error:
        raise RuntimeError(f"Docker returned invalid metadata for volume {volume_name}") from error
    expected = {
        "automaton.managed": "true",
        "automaton.resource": "hosted-runner-volume",
        "automaton.hosted-runner-id": runner_id,
        "automaton.storage-scope": scope,
    }
    if any(labels.get(key) != value for key, value in expected.items()):
        raise RuntimeError(f"Docker volume {volume_name} does not have the expected Automaton ownership labels")
    volumes_root = f"{docker_root}/volumes/"
    normalized_mountpoint = posixpath.normpath(mountpoint) if isinstance(mountpoint, str) else ""
    if not isinstance(mountpoint, str) or not normalized_mountpoint.startswith(volumes_root.rstrip("/") + "/"):
        raise RuntimeError("Docker returned an unsupported managed-volume mountpoint")
    return mountpoint


def measure(mountpoint: str) -> int:
    result = host("du", "-s", "-B1", "-x", "--", mountpoint)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or "Managed volume measurement failed")
    try:
        value = int(result.stdout.split()[0])
    except (ValueError, IndexError) as error:
        raise RuntimeError("Host returned an invalid managed volume measurement") from error
    if value < 0:
        raise RuntimeError("Host returned a negative managed volume measurement")
    return value


def main() -> None:
    request = json.load(sys.stdin)
    runner_uuid = uuid.UUID(str(request["hostedRunnerId"]))
    uuid.UUID(str(request["runnerHostId"]))
    runner_id = str(runner_uuid)
    compact = runner_uuid.hex
    expected_names = {
        "system": f"automaton-hosted-runner-{compact}-system",
        "user": f"automaton-hosted-runner-{compact}-user",
    }
    supplied = {
        "system": str(request["expectedSystemVolumeName"]),
        "user": str(request["expectedUserVolumeName"]),
    }
    if supplied != expected_names:
        raise ValueError("Only deterministic Automaton hosted Runner volume names are accepted")

    # Validate every target before measuring either one.
    docker_root = resolve_docker_root()
    mountpoints = {
        scope: validate_and_resolve(supplied[scope], runner_id, scope, docker_root)
        for scope in ("system", "user")
    }
    system_bytes = measure(mountpoints["system"])
    user_bytes = measure(mountpoints["user"])
    print(json.dumps({
        "success": True,
        "systemBytes": system_bytes,
        "userBytes": user_bytes,
        "totalBytes": system_bytes + user_bytes,
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
