#!/usr/bin/env python3
import json
import re
import subprocess
import sys

request = json.load(sys.stdin)
runner_id = str(request["hostedRunnerId"])
try:
    runner_id = str(__import__("uuid").UUID(runner_id))
except ValueError as error:
    raise ValueError("hostedRunnerId must be a UUID") from error
container_name = str(request["expectedContainerName"])
if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,159}", container_name):
    raise ValueError("containerName contains unsupported characters")
probe = subprocess.run(["automaton-host-exec", "docker", "container", "inspect", container_name], check=False,
                        text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
if probe.returncode != 0:
    raise RuntimeError(probe.stderr.strip() or "Hosted Runner container inspection failed")
try:
    labels = json.loads(probe.stdout)[0]["Config"]["Labels"] or {}
except (json.JSONDecodeError, IndexError, KeyError, TypeError) as error:
    raise RuntimeError("Docker returned invalid hosted Runner metadata") from error
expected = {"automaton.managed": "true", "automaton.resource": "hosted-runner", "automaton.hosted-runner-id": runner_id}
if any(labels.get(key) != value for key, value in expected.items()):
    raise RuntimeError("Docker container does not have the expected Automaton hosted Runner ownership labels")
result = subprocess.run(["automaton-host-exec", "docker", "start", container_name], check=False,
                        text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
if result.returncode != 0:
    raise RuntimeError(result.stderr.strip() or f"Host command failed with exit status {result.returncode}")
print(json.dumps({"success": True, "containerName": container_name}, separators=(",", ":")))
