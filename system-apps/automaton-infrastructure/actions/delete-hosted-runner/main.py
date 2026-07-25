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


def main() -> None:
    request = json.load(sys.stdin)
    runner_id = uuid.UUID(str(request["hostedRunnerId"])).hex
    container_name = str(request["containerName"])
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,159}", container_name):
        raise ValueError("containerName contains unsupported characters")
    probe = host("docker", "container", "inspect", container_name)
    if probe.returncode == 0:
        result = host("docker", "rm", "-f", container_name)
        if result.returncode != 0:
            raise RuntimeError(result.stderr.strip() or f"Host command failed with exit status {result.returncode}")
    elif "No such" not in probe.stderr:
        raise RuntimeError(probe.stderr.strip() or "Docker container inspection failed")
    preserve_volumes = request.get("preserveVolumes")
    if not isinstance(preserve_volumes, bool):
        raise ValueError("preserveVolumes must explicitly be true or false")
    if not preserve_volumes:
        for field in ("systemVolumeName", "userVolumeName"):
            volume_name = str(request[field])
            expected_scope = "system" if field == "systemVolumeName" else "user"
            if volume_name != f"automaton-hosted-runner-{runner_id}-{expected_scope}":
                raise ValueError(f"{field} is not an Automaton hosted Runner volume name")
            result = host("docker", "volume", "rm", volume_name)
            if result.returncode != 0 and "No such volume" not in result.stderr:
                raise RuntimeError(result.stderr.strip() or f"Could not remove {field}")
    print(json.dumps({"success": True, "containerName": container_name}, separators=(",", ":")))


if __name__ == "__main__":
    main()
