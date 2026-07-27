#!/usr/bin/env python3
import json
import os
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


def inspect_volume(volume_name: str) -> dict[str, str] | None:
    result = host("docker", "volume", "inspect", volume_name, check=False)
    if result.returncode != 0:
        if "no such volume" in result.stderr.lower():
            return None
        raise RuntimeError(result.stderr.strip() or f"Could not inspect Docker volume {volume_name}")
    try:
        payload = json.loads(result.stdout)
        labels = payload[0]["Labels"]
    except (json.JSONDecodeError, IndexError, KeyError, TypeError) as error:
        raise RuntimeError(f"Docker returned invalid metadata for volume {volume_name}") from error
    return labels or {}


def ensure_owned_volume(volume_name: str, runner_id: str, storage_scope: str) -> None:
    expected_labels = {
        "automaton.managed": "true",
        "automaton.resource": "hosted-runner-volume",
        "automaton.hosted-runner-id": runner_id,
        "automaton.storage-scope": storage_scope,
    }
    labels = inspect_volume(volume_name)
    if labels is None:
        label_arguments = [
            argument
            for key, value in expected_labels.items()
            for argument in ("--label", f"{key}={value}")
        ]
        host("docker", "volume", "create", *label_arguments, volume_name)
        labels = inspect_volume(volume_name)
    if labels is None or any(labels.get(key) != value for key, value in expected_labels.items()):
        raise RuntimeError(f"Docker volume {volume_name} does not have the expected Automaton ownership labels")


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
    enrollment_token = os.environ.get("AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN", "")
    if not enrollment_token.startswith("re_"):
        raise ValueError("Runner enrollment token was not provided")

    memory_mb = require_positive_int(request["memoryMb"], "memoryMb")
    reservation_mb = require_positive_int(request["memoryReservationMb"], "memoryReservationMb")
    pids_limit = require_positive_int(request["pidsLimit"], "pidsLimit")
    shared_memory_mb = require_positive_int(request["sharedMemoryMb"], "sharedMemoryMb")
    log_max_size_mb = require_positive_int(request["dockerLogMaxSizeMb"], "dockerLogMaxSizeMb")
    log_max_files = require_positive_int(request["dockerLogMaxFiles"], "dockerLogMaxFiles")
    runner_port = require_positive_int(request["runnerPort"], "runnerPort")
    cpu_limit = float(request["cpuLimit"])
    if cpu_limit <= 0:
        raise ValueError("cpuLimit must be positive")

    ensure_owned_volume(system_volume, runner_id, "system")
    ensure_owned_volume(user_volume, runner_id, "user")
    if host("docker", "network", "inspect", network, check=False).returncode != 0:
        host("docker", "network", "create", network)
    if runtime_network and host("docker", "network", "inspect", runtime_network, check=False).returncode != 0:
        host("docker", "network", "create", runtime_network)
    host("docker", "rm", "-f", container_name, check=False)

    docker_arguments = [
        "docker", "run", "-d",
        "--name", container_name,
        "--restart", "unless-stopped",
        "--cpus", str(cpu_limit),
        "--memory", f"{memory_mb}m",
        "--memory-swap", f"{memory_mb}m",
        "--memory-reservation", f"{reservation_mb}m",
        "--pids-limit", str(pids_limit),
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
