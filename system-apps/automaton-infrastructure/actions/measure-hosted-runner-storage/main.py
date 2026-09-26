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


def resolve_filesystem_mountpoint(path: str) -> str:
    result = host("findmnt", "-no", "TARGET", "-T", path)
    filesystem_mountpoint = result.stdout.strip()
    if not filesystem_mountpoint.startswith("/") or any(char.isspace() for char in filesystem_mountpoint):
        raise RuntimeError("Could not resolve the XFS filesystem mountpoint")
    return posixpath.normpath(filesystem_mountpoint)


def resolve_filesystem_uuid(path: str) -> str:
    result = host("findmnt", "-no", "UUID", "-T", path)
    try:
        return str(uuid.UUID(result.stdout.strip()))
    except (ValueError, AttributeError) as error:
        raise RuntimeError("Could not resolve a stable UUID for the Hosted Runner XFS filesystem") from error


def require_expected_filesystem_uuid(value: object) -> str | None:
    if value is None:
        return None
    try:
        return str(uuid.UUID(str(value)))
    except (ValueError, AttributeError) as error:
        raise ValueError("storageFilesystemUuid must be a valid filesystem UUID") from error


def project_quota_enforcement_enabled(output: str) -> bool:
    lowered = output.lower()
    if "project quota state: enforcement" in lowered:
        return True
    return re.search(r"project quota state[\s\S]*?\benforcement:\s*on\b", lowered) is not None


def require_xfs_project_quota(mountpoint: str) -> str:
    filesystem = host("stat", "-f", "-c", "%T", mountpoint)
    if filesystem.stdout.strip().lower() != "xfs":
        raise RuntimeError("Hard Hosted Runner storage quotas require an XFS Docker volume filesystem")
    options = host("findmnt", "-no", "OPTIONS", "-T", mountpoint)
    if not {part.strip().lower() for part in options.stdout.split(",")}.intersection({"prjquota", "pquota"}):
        raise RuntimeError("XFS project quota accounting and enforcement are not enabled")
    filesystem_mountpoint = resolve_filesystem_mountpoint(mountpoint)
    state = host("xfs_quota", "-x", "-c", "state", filesystem_mountpoint)
    if not project_quota_enforcement_enabled(state.stdout + state.stderr):
        raise RuntimeError("XFS project quota enforcement could not be verified")
    return filesystem_mountpoint


def parse_project_quota_report(output: str, project_id: int) -> tuple[int, int] | None:
    saw_report_shape = False
    for line in output.splitlines():
        stripped = line.strip()
        lowered = stripped.lower()
        if lowered.startswith("project quota on") or lowered.startswith("project id"):
            saw_report_shape = True
            continue
        match = re.match(r"^#?(\d+)\s+(.+)$", stripped)
        if not match:
            continue
        saw_report_shape = True
        row_project_id = int(match.group(1))
        if row_project_id != project_id:
            continue
        columns = match.group(2).split()
        if len(columns) < 3 or any(not re.fullmatch(r"\d+", value) for value in columns[:3]):
            raise RuntimeError(f"XFS project quota for project {project_id} could not be verified: malformed output")
        return int(columns[0]) * 1024, int(columns[2]) * 1024
    if saw_report_shape:
        return None
    raise RuntimeError(f"XFS project quota for project {project_id} could not be verified: output could not be parsed")


def read_project_quota(filesystem_mountpoint: str, project_id: int) -> tuple[int, int] | None:
    result = host("xfs_quota", "-x", "-c", "report -p -b -n", filesystem_mountpoint)
    return parse_project_quota_report(result.stdout + result.stderr, project_id)


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
    filesystem_mountpoints = {
        scope: require_xfs_project_quota(mountpoint)
        for scope, mountpoint in mountpoints.items()
    }
    if len(set(filesystem_mountpoints.values())) != 1:
        raise RuntimeError("Hosted Runner storage volumes must share one XFS filesystem")
    filesystem_mountpoint = next(iter(filesystem_mountpoints.values()))
    storage_filesystem_uuid = resolve_filesystem_uuid(filesystem_mountpoint)
    expected_filesystem_uuid = require_expected_filesystem_uuid(request.get("storageFilesystemUuid"))
    if expected_filesystem_uuid is not None and storage_filesystem_uuid != expected_filesystem_uuid:
        raise RuntimeError("Hosted Runner storage is on a different XFS filesystem; explicit storage migration is required")
    quota = read_project_quota(filesystem_mountpoint, project_id)
    if quota is None:
        raise RuntimeError(f"XFS project quota for project {project_id} could not be verified")
    total_bytes, _ = quota
    print(json.dumps({
        "success": True,
        "systemBytes": 0,
        "userBytes": total_bytes,
        "totalBytes": total_bytes,
        "usageSource": "xfs-project-quota",
        "storageFilesystemUuid": storage_filesystem_uuid,
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
