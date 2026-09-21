#!/usr/bin/env python3
import json
import os
import posixpath
import re
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
    if (not isinstance(mountpoint, str)
            or normalized_mountpoint != mountpoint
            or not normalized_mountpoint.startswith(volumes_root.rstrip("/") + "/")):
        raise RuntimeError("Docker returned an unsupported managed-volume mountpoint")
    return mountpoint


def require_project_id(value: object) -> int:
    project_id = int(value)
    if project_id <= 0 or project_id > 2_147_483_647:
        raise ValueError("xfsProjectId must be a positive signed 32-bit integer")
    return project_id


def require_xfs_project_quota(mountpoint: str) -> None:
    filesystem = host("stat", "-f", "-c", "%T", mountpoint)
    if filesystem.stdout.strip().lower() != "xfs":
        raise RuntimeError("Hard Hosted Runner storage quotas require an XFS Docker volume filesystem")
    options = host("findmnt", "-no", "OPTIONS", "-T", mountpoint)
    if not {part.strip().lower() for part in options.stdout.split(",")}.intersection({"prjquota", "pquota"}):
        raise RuntimeError("XFS project quota accounting and enforcement are not enabled")
    state = host("xfs_quota", "-x", "-c", "state", mountpoint)
    text = (state.stdout + state.stderr).lower()
    if "project quota" not in text or "enforc" not in text:
        raise RuntimeError("XFS project quota enforcement could not be verified")


def measure_project(mountpoint: str, project_id: int) -> int:
    project = f"#{project_id}"
    result = host("xfs_quota", "-x", "-c", f"quota -p -b -N {project}", mountpoint)
    for line in (result.stdout + result.stderr).splitlines():
        if project not in line and str(project_id) not in line:
            continue
        values = [int(value) for value in re.findall(r"(?<![A-Za-z])\d+(?![A-Za-z])", line)]
        if len(values) >= 2:
            used_blocks = values[1] if values[0] == project_id else values[0]
            return used_blocks * 1024
    return 0


def main() -> None:
    request = json.load(sys.stdin)
    runner_uuid = uuid.UUID(str(request["hostedRunnerId"]))
    uuid.UUID(str(request["runnerHostId"]))
    project_id = require_project_id(request["xfsProjectId"])
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
    for mountpoint in mountpoints.values():
        require_xfs_project_quota(mountpoint)
    total_bytes = measure_project(mountpoints["system"], project_id)
    print(json.dumps({
        "success": True,
        "systemBytes": 0,
        "userBytes": total_bytes,
        "totalBytes": total_bytes,
        "usageSource": "xfs-project-quota",
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
