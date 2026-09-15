#!/usr/bin/env python3
import json
import re
import subprocess
import sys
import uuid
from dataclasses import dataclass


def host(*arguments: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["automaton-host-exec", *arguments],
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )


@dataclass(frozen=True)
class DockerContainer:
    container_id: str
    name: str
    labels: dict[str, str]
    mounted_volumes: frozenset[str]


@dataclass(frozen=True)
class OwnedContainer:
    container_id: str
    name: str
    role: str
    mounted_volumes: frozenset[str]


def is_missing_volume(result: subprocess.CompletedProcess[str]) -> bool:
    error = result.stderr.lower()
    return "no such volume" in error or "no such object" in error


def is_missing_container(result: subprocess.CompletedProcess[str]) -> bool:
    error = result.stderr.lower()
    return "no such container" in error or "no such object" in error


def inspect_owned_volume(
    volume_name: str,
    runner_id: str,
    storage_scope: str,
) -> bool:
    result = host("docker", "volume", "inspect", volume_name)
    if result.returncode != 0:
        if is_missing_volume(result):
            return False
        raise RuntimeError(result.stderr.strip() or f"Could not inspect Docker volume {volume_name}")
    try:
        inspected = json.loads(result.stdout)
        if not isinstance(inspected, list) or len(inspected) != 1:
            raise ValueError("expected one inspected volume")
        volume = inspected[0]
        if volume.get("Name") != volume_name:
            raise ValueError("volume name did not match")
        labels = volume.get("Labels") or {}
        if not isinstance(labels, dict):
            raise ValueError("volume labels were invalid")
    except (json.JSONDecodeError, ValueError, AttributeError, TypeError) as error:
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


def parse_container_inspection(result: subprocess.CompletedProcess[str], reference: str) -> DockerContainer | None:
    if result.returncode != 0:
        if is_missing_container(result):
            return None
        raise RuntimeError(result.stderr.strip() or f"Could not inspect Docker container {reference}")
    try:
        container = json.loads(result.stdout)
        if not isinstance(container, dict):
            raise ValueError("expected one inspected container")
        container_id = container.get("Id")
        name = container.get("Name")
        config = container.get("Config")
        mounts = container.get("Mounts")
        if not isinstance(container_id, str) or re.fullmatch(r"[0-9a-f]{64}", container_id) is None:
            raise ValueError("container ID was invalid")
        if not isinstance(name, str) or not isinstance(config, dict) or not isinstance(mounts, list):
            raise ValueError("container metadata was incomplete")
        labels = config.get("Labels")
        if not isinstance(labels, dict) or any(not isinstance(key, str) or not isinstance(value, str) for key, value in labels.items()):
            raise ValueError("container labels were invalid")
        if any(not isinstance(mount, dict) or not isinstance(mount.get("Type"), str) for mount in mounts):
            raise ValueError("container mounts were invalid")
        mounted_volumes = set()
        for mount in mounts:
            if mount["Type"] != "volume":
                continue
            volume_name = mount.get("Name")
            if not isinstance(volume_name, str) or not volume_name:
                raise ValueError("container volume mount name was invalid")
            mounted_volumes.add(volume_name)
        return DockerContainer(
            container_id=container_id,
            name=name.lstrip("/"),
            labels=labels,
            mounted_volumes=frozenset(mounted_volumes),
        )
    except (json.JSONDecodeError, ValueError, TypeError) as error:
        raise RuntimeError(f"Docker returned invalid metadata for container {reference}") from error


def normalize_guid(value: object) -> str | None:
    if not isinstance(value, str):
        return None
    try:
        return str(uuid.UUID(value))
    except ValueError:
        return None


def validate_hosted_runner_container(
    container: DockerContainer,
    runner_id: str,
    container_name: str,
) -> OwnedContainer:
    labels = container.labels
    name = container.name
    if name != container_name:
        raise RuntimeError(f"Docker container {name} does not match the requested Hosted Runner container name")
    expected = {
        "automaton.managed": "true",
        "automaton.resource": "hosted-runner",
        "automaton.hosted-runner-id": runner_id,
    }
    if any(labels.get(key) != value for key, value in expected.items()):
        raise RuntimeError(f"Docker container {name} does not have the expected Hosted Runner ownership labels")
    return OwnedContainer(
        container_id=container.container_id,
        name=name,
        role="hosted-runner",
        mounted_volumes=container.mounted_volumes,
    )


def validate_application_container(container: DockerContainer, runner_id: str) -> OwnedContainer:
    labels = container.labels
    name = container.name
    label_sets = (
        ("com.automaton.", "generic"),
        ("automaton.", "build"),
    )
    for prefix, expected_kind in label_sets:
        managed = labels.get(f"{prefix}managed")
        kind = labels.get(f"{prefix}runtime-kind")
        if managed != "true" or kind != expected_kind:
            continue
        app_id = normalize_guid(labels.get(f"{prefix}app-id"))
        deployment_target_id = normalize_guid(labels.get(f"{prefix}deployment-target-id"))
        hosted_runner_id = normalize_guid(labels.get(f"{prefix}hosted-runner-id"))
        runner_host_id = normalize_guid(labels.get(f"{prefix}runner-host-id"))
        if (app_id is None or deployment_target_id is None or runner_host_id is None
                or hosted_runner_id != runner_id):
            break
        return OwnedContainer(
            container_id=container.container_id,
            name=name,
            role="application",
            mounted_volumes=container.mounted_volumes,
        )
    raise RuntimeError(f"Docker volume consumer {name} does not have matching Automaton app and Hosted Runner ownership labels")


def inspect_container(reference: str) -> DockerContainer | None:
    result = host("docker", "container", "inspect", "--format", "{{json .}}", reference)
    return parse_container_inspection(result, reference)


def discover_owned_containers(
    runner_id: str,
    container_name: str,
    volume_names: list[str],
) -> list[OwnedContainer]:
    resources: dict[str, OwnedContainer] = {}
    hosted_runner = inspect_container(container_name)
    if hosted_runner is not None:
        resource = validate_hosted_runner_container(hosted_runner, runner_id, container_name)
        resources[resource.container_id] = resource

    for volume_name in volume_names:
        result = host(
            "docker", "container", "ls", "--all", "--quiet", "--no-trunc",
            "--filter", f"volume={volume_name}",
        )
        if result.returncode != 0:
            raise RuntimeError(result.stderr.strip() or f"Could not list consumers of Docker volume {volume_name}")
        for container_id in sorted(set(result.stdout.splitlines())):
            if not container_id:
                continue
            if re.fullmatch(r"[0-9a-f]{64}", container_id) is None:
                raise RuntimeError(f"Docker returned an invalid container ID for volume {volume_name}")
            inspected = inspect_container(container_id)
            if inspected is None:
                continue
            mounted_volumes = inspected.mounted_volumes
            if volume_name not in mounted_volumes:
                continue
            name = inspected.name
            if name == container_name or inspected.labels.get("automaton.resource") == "hosted-runner":
                resource = validate_hosted_runner_container(inspected, runner_id, container_name)
            else:
                resource = validate_application_container(inspected, runner_id)
            previous = resources.get(resource.container_id)
            if previous is not None and previous != resource:
                raise RuntimeError(f"Docker returned inconsistent ownership metadata for container {resource.name}")
            resources[resource.container_id] = resource
    return list(resources.values())


def remove_container(container: OwnedContainer) -> None:
    result = host("docker", "rm", "-f", container.container_id)
    if result.returncode != 0 and not is_missing_container(result):
        raise RuntimeError(
            result.stderr.strip()
            or f"Could not remove Automaton-owned container {container.name} ({container.container_id})"
        )


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

    volume_names = []
    for field, scope in (("systemVolumeName", "system"), ("userVolumeName", "user")):
        volume_name = str(request[field])
        if volume_name != f"automaton-hosted-runner-{runner_id_compact}-{scope}":
            raise ValueError(f"{field} is not an Automaton hosted Runner volume name")
        if inspect_owned_volume(volume_name, runner_id, scope):
            volume_names.append(volume_name)

    # Discover and validate every volume consumer before removing any resource.
    containers = discover_owned_containers(runner_id, container_name, volume_names)
    for container in containers:
        if container.role == "application":
            remove_container(container)
    for container in containers:
        if container.role == "hosted-runner":
            remove_container(container)

    if not preserve_volumes:
        for volume_name in volume_names:
            scope = "system" if volume_name.endswith("-system") else "user"
            if not inspect_owned_volume(volume_name, runner_id, scope):
                continue
            result = host("docker", "volume", "rm", volume_name)
            if result.returncode != 0 and not is_missing_volume(result):
                raise RuntimeError(
                    f"Could not remove owned Docker volume {volume_name}: "
                    f"{result.stderr.strip() or f'Host command failed with exit status {result.returncode}'}"
                )
    print(json.dumps({"success": True, "containerName": container_name}, separators=(",", ":")))


if __name__ == "__main__":
    main()
