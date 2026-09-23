#!/usr/bin/env python3
import json
import ipaddress
import os
import posixpath
import re
import subprocess
import sys
import uuid


SAFE_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,159}$")
SAFE_RUNNER_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9 _.\-]{0,159}$")


def require_name(value: object, field: str) -> str:
    text = str(value or "")
    if not SAFE_NAME.fullmatch(text):
        raise ValueError(f"{field} contains unsupported characters")
    return text


def require_runner_name(value: object) -> str:
    text = str(value or "")
    if not SAFE_RUNNER_NAME.fullmatch(text):
        raise ValueError("runnerName contains unsupported characters")
    return text


def require_positive_int(value: object, field: str) -> int:
    number = int(value)
    if number <= 0:
        raise ValueError(f"{field} must be positive")
    return number


def host(*arguments: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        ["automaton-host-exec", *arguments],
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if check and result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or f"Host command failed with exit status {result.returncode}")
    return result


def inspect_volume(volume_name: str) -> dict[str, object] | None:
    result = host("docker", "volume", "inspect", volume_name, check=False)
    if result.returncode != 0:
        if "no such volume" in result.stderr.lower():
            return None
        raise RuntimeError(result.stderr.strip() or f"Could not inspect Docker volume {volume_name}")
    try:
        payload = json.loads(result.stdout)
        metadata = payload[0]
        labels = metadata["Labels"]
    except (json.JSONDecodeError, IndexError, KeyError, TypeError) as error:
        raise RuntimeError(f"Docker returned invalid metadata for volume {volume_name}") from error
    return {"labels": labels or {}, "mountpoint": metadata.get("Mountpoint")}


def ensure_owned_volume(volume_name: str, runner_id: str, storage_scope: str) -> str:
    expected_labels = {
        "automaton.managed": "true",
        "automaton.resource": "hosted-runner-volume",
        "automaton.hosted-runner-id": runner_id,
        "automaton.storage-scope": storage_scope,
    }
    metadata = inspect_volume(volume_name)
    if metadata is None:
        label_arguments = [
            argument
            for key, value in expected_labels.items()
            for argument in ("--label", f"{key}={value}")
        ]
        host("docker", "volume", "create", *label_arguments, volume_name)
        metadata = inspect_volume(volume_name)
    labels = metadata.get("labels", {}) if metadata else {}
    mountpoint = metadata.get("mountpoint") if metadata else None
    if not isinstance(labels, dict) or any(labels.get(key) != value for key, value in expected_labels.items()):
        raise RuntimeError(f"Docker volume {volume_name} does not have the expected Automaton ownership labels")
    if not isinstance(mountpoint, str) or not mountpoint.startswith("/"):
        raise RuntimeError(f"Docker volume {volume_name} does not expose a host mountpoint")
    return mountpoint


def require_positive_project_id(value: object) -> int:
    project_id = int(value)
    if project_id <= 0 or project_id > 2_147_483_647:
        raise ValueError("xfsProjectId must be a positive signed 32-bit integer")
    return project_id


def resolve_docker_root() -> str:
    result = host("docker", "info", "--format", "{{.DockerRootDir}}")
    root = result.stdout.strip().rstrip("/")
    if result.returncode != 0 or not root.startswith("/") or any(char.isspace() for char in root):
        raise RuntimeError("Could not resolve the host Docker data root")
    return root


def validate_mountpoint(mountpoint: str, docker_root: str) -> None:
    expected_root = f"{docker_root}/volumes/"
    normalized = posixpath.normpath(mountpoint)
    if normalized != mountpoint or not normalized.startswith(expected_root) or "/../" in normalized:
        raise RuntimeError("Managed Hosted Runner storage must remain under the Docker volume root")


def current_project_id(mountpoint: str) -> int | None:
    result = host("xfs_io", "-c", "stat", mountpoint)
    output = result.stdout + result.stderr
    match = re.search(r"projid\s*[.= :]\s*(\d+)", output, re.IGNORECASE)
    return int(match.group(1)) if match else None


def project_quota_enforcement_enabled(output: str) -> bool:
    lowered = output.lower()
    if "project quota state: enforcement" in lowered:
        return True
    return re.search(r"project quota state[\s\S]*?\benforcement:\s*on\b", lowered) is not None


def resolve_filesystem_mountpoint(path: str) -> str:
    result = host("findmnt", "-no", "TARGET", "-T", path)
    mountpoint = result.stdout.strip()
    if not mountpoint.startswith("/") or any(char.isspace() for char in mountpoint):
        raise RuntimeError("Could not resolve the XFS filesystem mountpoint")
    return posixpath.normpath(mountpoint)


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


def has_other_runner_volume_for_project(
    docker_root: str,
    filesystem_mountpoint: str,
    runner_id: str,
    project_id: int,
) -> bool:
    result = host(
        "docker",
        "volume",
        "ls",
        "--filter",
        "label=automaton.managed=true",
        "--filter",
        "label=automaton.resource=hosted-runner-volume",
        "--format",
        "{{.Name}}",
    )
    for volume_name in result.stdout.splitlines():
        if not volume_name.strip():
            continue
        metadata = inspect_volume(require_name(volume_name.strip(), "volumeName"))
        if metadata is None:
            continue
        labels = metadata.get("labels", {})
        if not isinstance(labels, dict):
            raise RuntimeError("Managed Hosted Runner volume labels could not be verified")
        if labels.get("automaton.managed") != "true" or labels.get("automaton.resource") != "hosted-runner-volume":
            continue
        if labels.get("automaton.hosted-runner-id") == runner_id:
            continue
        mountpoint = metadata.get("mountpoint")
        if not isinstance(mountpoint, str) or not mountpoint.startswith("/"):
            raise RuntimeError(f"Managed Hosted Runner storage volume {volume_name} has no valid mountpoint")
        validate_mountpoint(mountpoint, docker_root)
        if resolve_filesystem_mountpoint(mountpoint) != filesystem_mountpoint:
            continue
        other_project_id = current_project_id(mountpoint)
        if other_project_id is None:
            raise RuntimeError(f"XFS project identity could not be verified for managed volume {volume_name}")
        if other_project_id == project_id:
            return True
    return False


def ensure_xfs_project_quota(mountpoints: list[str], project_id: int, disk_limit_mb: int, runner_id: str) -> None:
    if disk_limit_mb <= 0:
        raise ValueError("diskLimitMb must be positive")
    docker_root = resolve_docker_root()
    filesystem_mountpoints: list[str] = []
    for mountpoint in mountpoints:
        validate_mountpoint(mountpoint, docker_root)
        filesystem = host("stat", "-f", "-c", "%T", mountpoint)
        if filesystem.stdout.strip().lower() != "xfs":
            raise RuntimeError("Hard Hosted Runner storage quotas require an XFS Docker volume filesystem")
        options = host("findmnt", "-no", "OPTIONS", "-T", mountpoint)
        quota_options = {part.strip().lower() for part in options.stdout.split(",")}
        if not {"prjquota", "pquota"}.intersection(quota_options):
            raise RuntimeError("XFS project quota accounting and enforcement are not enabled")
        filesystem_mountpoint = resolve_filesystem_mountpoint(mountpoint)
        state = host("xfs_quota", "-x", "-c", "state", filesystem_mountpoint)
        if not project_quota_enforcement_enabled(state.stdout + state.stderr):
            raise RuntimeError("XFS project quota enforcement could not be verified")
        existing_project = current_project_id(mountpoint)
        if existing_project not in (None, 0, project_id):
            raise RuntimeError(f"Managed volume {mountpoint} belongs to a different XFS project")
        filesystem_mountpoints.append(filesystem_mountpoint)

    if len(set(filesystem_mountpoints)) != 1:
        raise RuntimeError("Hosted Runner storage volumes must share one XFS filesystem")
    filesystem_mountpoint = filesystem_mountpoints[0]
    for mountpoint in mountpoints:
        host("xfs_quota", "-x", "-c", f"project -s -p {mountpoint} {project_id}", filesystem_mountpoint)

    for mountpoint in mountpoints:
        if current_project_id(mountpoint) != project_id:
            raise RuntimeError(f"XFS project {project_id} was not assigned to managed volume {mountpoint}")

    existing_quota = read_project_quota(filesystem_mountpoint, project_id)
    limit_bytes = disk_limit_mb * 1024 * 1024
    if existing_quota is None or existing_quota[1] != limit_bytes:
        if has_other_runner_volume_for_project(docker_root, filesystem_mountpoint, runner_id, project_id):
            raise RuntimeError(
                "The requested disk limit differs from the current or missing XFS project quota used by another Hosted Runner on this filesystem; "
                "move or delete the co-located Hosted Runner before changing the shared project limit"
            )
    if existing_quota is not None and existing_quota[0] > limit_bytes:
        raise RuntimeError("Disk limit is below current XFS project usage; data was not deleted")
    host("xfs_quota", "-x", "-c", f"limit -p bhard={disk_limit_mb}m {project_id}", filesystem_mountpoint)
    configured_quota = read_project_quota(filesystem_mountpoint, project_id)
    if configured_quota is None:
        raise RuntimeError(f"XFS project quota for project {project_id} was not configured")
    _, hard_limit_bytes = configured_quota
    if hard_limit_bytes != limit_bytes:
        raise RuntimeError("XFS project hard limit did not match diskLimitMb")


def verify_xfs_project_quota(mountpoints: list[str], project_id: int, disk_limit_mb: int) -> None:
    """Verify the final Docker-backed roots after all container mutations."""
    if disk_limit_mb <= 0:
        raise ValueError("diskLimitMb must be positive")
    docker_root = resolve_docker_root()
    filesystem_mountpoints: list[str] = []
    for mountpoint in mountpoints:
        validate_mountpoint(mountpoint, docker_root)
        filesystem = host("stat", "-f", "-c", "%T", mountpoint)
        if filesystem.stdout.strip().lower() != "xfs":
            raise RuntimeError("Hard Hosted Runner storage quotas require an XFS Docker volume filesystem")
        options = host("findmnt", "-no", "OPTIONS", "-T", mountpoint)
        quota_options = {part.strip().lower() for part in options.stdout.split(",")}
        if not {"prjquota", "pquota"}.intersection(quota_options):
            raise RuntimeError("XFS project quota accounting and enforcement are not enabled")
        filesystem_mountpoint = resolve_filesystem_mountpoint(mountpoint)
        state = host("xfs_quota", "-x", "-c", "state", filesystem_mountpoint)
        if not project_quota_enforcement_enabled(state.stdout + state.stderr):
            raise RuntimeError("XFS project quota enforcement could not be verified")
        if current_project_id(mountpoint) != project_id:
            raise RuntimeError(f"XFS project {project_id} was not assigned to final managed volume {mountpoint}")
        filesystem_mountpoints.append(filesystem_mountpoint)

    if len(set(filesystem_mountpoints)) != 1:
        raise RuntimeError("Hosted Runner storage volumes must share one XFS filesystem")
    final_quota = read_project_quota(filesystem_mountpoints[0], project_id)
    if final_quota is None:
        raise RuntimeError(f"XFS project quota for project {project_id} could not be verified")
    usage, hard_limit_bytes = final_quota
    limit_bytes = disk_limit_mb * 1024 * 1024
    if usage > limit_bytes:
        raise RuntimeError("XFS project usage exceeds diskLimitMb")
    if hard_limit_bytes != limit_bytes:
        raise RuntimeError("XFS project hard limit did not match diskLimitMb on final volumes")


def ensure_resource_group(runner_id: str, cpu_limit: float, memory_mb: int, pids_limit: int) -> str:
    info = host("docker", "info", "--format", "{{.CgroupVersion}}|{{.CgroupDriver}}")
    version, _, driver = info.stdout.strip().partition("|")
    if version != "2" or driver != "systemd":
        raise RuntimeError("Aggregate Hosted Runner CPU, memory, and PID limits require cgroup v2 with Docker systemd cgroups")
    slice_name = f"automaton-hosted-runner-{runner_id.replace('-', '')}.slice"
    cpu_percent = format(cpu_limit * 100, ".6g") + "%"
    host("systemctl", "set-property", slice_name, f"CPUQuota={cpu_percent}", f"MemoryMax={memory_mb}M", "MemorySwapMax=0", f"TasksMax={pids_limit}")
    shown = host("systemctl", "show", slice_name, "--property=CPUQuotaPerSecUSec,MemoryMax,TasksMax", "--value")
    if shown.returncode != 0:
        raise RuntimeError("The Hosted Runner systemd resource group could not be verified")
    return slice_name


def main() -> None:
    request = json.load(sys.stdin)
    runner_id = str(uuid.UUID(str(request["hostedRunnerId"])))
    runner_id_compact = runner_id.replace("-", "")
    container_name = require_name(request["containerName"], "containerName")
    runner_name = require_runner_name(request["runnerName"])
    image = str(request["image"])
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._/:@-]{0,299}", image):
        raise ValueError("image is invalid")
    network = require_name(request["networkName"], "networkName")
    runtime_network = request.get("runtimeNetworkName")
    if runtime_network:
        runtime_network = require_name(runtime_network, "runtimeNetworkName")
    system_volume = require_name(request["systemVolumeName"], "systemVolumeName")
    user_volume = require_name(request["userVolumeName"], "userVolumeName")
    if system_volume != f"automaton-hosted-runner-{runner_id_compact}-system":
        raise ValueError("systemVolumeName does not match hostedRunnerId")
    if user_volume != f"automaton-hosted-runner-{runner_id_compact}-user":
        raise ValueError("userVolumeName does not match hostedRunnerId")
    user_id = str(uuid.UUID(str(request["userId"])))
    specification_id = str(uuid.UUID(str(request["specificationId"])))
    api_url = str(request["apiUrl"])
    if not api_url.startswith(("https://", "http://")):
        raise ValueError("apiUrl must be an HTTP(S) URL")
    runtime_host_address = str(ipaddress.ip_address(str(request["runtimeHostAddress"])))
    if not ipaddress.ip_address(runtime_host_address).is_private or ipaddress.ip_address(runtime_host_address).is_loopback:
        raise ValueError("runtimeHostAddress must be a concrete private address")
    enrollment_token = os.environ.get("AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN", "")
    if not enrollment_token.startswith("re_"):
        raise ValueError("Runner enrollment token was not provided")

    memory_mb = require_positive_int(request["memoryMb"], "memoryMb")
    reservation_mb = require_positive_int(request["memoryReservationMb"], "memoryReservationMb")
    pids_limit = require_positive_int(request["pidsLimit"], "pidsLimit")
    shared_memory_mb = require_positive_int(request["sharedMemoryMb"], "sharedMemoryMb")
    disk_limit_mb = require_positive_int(request["diskLimitMb"], "diskLimitMb")
    xfs_project_id = require_positive_project_id(request["xfsProjectId"])
    log_max_size_mb = require_positive_int(request["dockerLogMaxSizeMb"], "dockerLogMaxSizeMb")
    log_max_files = require_positive_int(request["dockerLogMaxFiles"], "dockerLogMaxFiles")
    runner_port = require_positive_int(request["runnerPort"], "runnerPort")
    cpu_limit = float(request["cpuLimit"])
    if cpu_limit <= 0:
        raise ValueError("cpuLimit must be positive")

    system_mountpoint = ensure_owned_volume(system_volume, runner_id, "system")
    user_mountpoint = ensure_owned_volume(user_volume, runner_id, "user")
    ensure_xfs_project_quota([system_mountpoint, user_mountpoint], xfs_project_id, disk_limit_mb, runner_id)
    cgroup_parent = ensure_resource_group(runner_id, cpu_limit, memory_mb, pids_limit)

    # Refresh mutable tags before changing any existing container. A failed
    # pull must leave the current hosted Runner available for retry.
    host("docker", "pull", image)
    if host("docker", "network", "inspect", network, check=False).returncode != 0:
        host("docker", "network", "create", network)
    if runtime_network and host("docker", "network", "inspect", runtime_network, check=False).returncode != 0:
        host("docker", "network", "create", runtime_network)
    host("docker", "rm", "-f", container_name, check=False)

    docker_arguments = [
        "docker", "run", "-d",
        "--name", container_name,
        "--restart", "unless-stopped",
        "--cgroup-parent", cgroup_parent,
        "--memory-reservation", f"{reservation_mb}m",
        "--shm-size", f"{shared_memory_mb}m",
        "--security-opt", "no-new-privileges=true",
        "--log-driver", "local",
        "--log-opt", f"max-size={log_max_size_mb}m",
        "--log-opt", f"max-file={log_max_files}",
        "--label", "automaton.managed=true",
        "--label", "automaton.resource=hosted-runner",
        "--label", f"automaton.hosted-runner-id={runner_id}",
        "--label", f"automaton.user-id={user_id}",
        "--label", f"automaton.specification-id={specification_id}",
        "--network", network,
        "-e", f"AUTOMATON_SERVER_URL={api_url}",
        "-e", f"AUTOMATON_ENROLLMENT_TOKEN={enrollment_token}",
        "-e", f"AUTOMATON_RUNNER_NAME={runner_name}",
        "-e", f"AUTOMATON_HOSTED_RUNNER_ID={runner_id}",
        "-e", "AUTOMATON_RUNNER_PURPOSE=Managed",
        "-e", "AUTOMATON_RUNNER_APPLICATION_PROFILE=Standard",
        "-e", f"AUTOMATON_RUNTIME_HOST_ADDRESS={runtime_host_address}",
        "-v", f"{system_volume}:/automaton/system",
        "-v", f"{user_volume}:/automaton/storage",
    ]
    published_port = request.get("publishedPort")
    if published_port is not None:
        published_port = require_positive_int(published_port, "publishedPort")
        docker_arguments.extend(["-p", f"{published_port}:{runner_port}"])
    docker_arguments.append(image)

    container_id = host(*docker_arguments).stdout.strip()
    if runtime_network and runtime_network != network:
        host("docker", "network", "connect", runtime_network, container_name, check=False)
    verify_xfs_project_quota([system_mountpoint, user_mountpoint], xfs_project_id, disk_limit_mb)

    response = {
        "success": True,
        "containerId": container_id,
        "containerName": container_name,
    }
    if published_port is not None:
        response["publishedPort"] = published_port
    print(json.dumps(response, separators=(",", ":")))


if __name__ == "__main__":
    main()
