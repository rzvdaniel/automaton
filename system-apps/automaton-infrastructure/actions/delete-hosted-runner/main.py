#!/usr/bin/env python3
import json
import re
import subprocess
import sys
import uuid

def host(*arguments: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["automaton-host-exec", *arguments],
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )


def inspect_owned_volume(
    volume_name: str,
    runner_id: str,
    storage_scope: str,
) -> bool:
    result = host("docker", "volume", "inspect", volume_name)
    if result.returncode != 0:
        if "No such volume" in result.stderr:
            return False
        raise RuntimeError(result.stderr.strip() or f"Could not inspect Docker volume {volume_name}")
    try:
        labels = json.loads(result.stdout)[0]["Labels"] or {}
    except (json.JSONDecodeError, IndexError, KeyError, TypeError) as error:
        raise RuntimeError(f"Docker returned invalid metadata for volume {volume_name}") from error
    expected_labels = {
        "automaton.managed": "true",
        "automaton.resource": "hosted-runner-volume",
        "automaton.hosted-runner-id": runner_id,
        "automaton.storage-scope": storage_scope,
    }
    if any(labels.get(key) != value for key, value in expected_labels.items()):
        raise RuntimeError(f"Docker volume {volume_name} does not have the expected Automaton ownership labels")
    return True


def remove_container(container_name: str) -> None:
    probe = host("docker", "container", "inspect", container_name)
    if probe.returncode == 0:
        result = host("docker", "rm", "-f", container_name)
        if result.returncode != 0:
            raise RuntimeError(result.stderr.strip() or f"Host command failed with exit status {result.returncode}")
    elif "No such" not in probe.stderr:
        raise RuntimeError(probe.stderr.strip() or "Docker container inspection failed")


def main() -> None:
    request = json.load(sys.stdin)
    runner_uuid = uuid.UUID(str(request["hostedRunnerId"]))
    runner_id = str(runner_uuid)
    runner_id_compact = runner_uuid.hex
    container_name = str(request["containerName"])
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,159}", container_name):
        raise ValueError("containerName contains unsupported characters")
    preserve_volumes = request.get("preserveVolumes")
    if not isinstance(preserve_volumes, bool):
        raise ValueError("preserveVolumes must explicitly be true or false")
    validated_volumes: list[str] = []
    if not preserve_volumes:
        for field in ("systemVolumeName", "userVolumeName"):
            volume_name = str(request[field])
            expected_scope = "system" if field == "systemVolumeName" else "user"
            if volume_name != f"automaton-hosted-runner-{runner_id_compact}-{expected_scope}":
                raise ValueError(f"{field} is not an Automaton hosted Runner volume name")
            if inspect_owned_volume(volume_name, runner_id, expected_scope):
                validated_volumes.append(volume_name)

    # Do not mutate the container or either volume until every destructive
    # target has passed its ownership checks.
    remove_container(container_name)
    for volume_name in validated_volumes:
        result = host("docker", "volume", "rm", volume_name)
        if result.returncode != 0 and "No such volume" not in result.stderr:
            raise RuntimeError(result.stderr.strip() or f"Could not remove Docker volume {volume_name}")
    print(json.dumps({"success": True, "containerName": container_name}, separators=(",", ":")))


if __name__ == "__main__":
    main()
