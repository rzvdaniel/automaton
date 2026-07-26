#!/usr/bin/env python3
import json
import re
import subprocess
import sys

request = json.load(sys.stdin)
container_name = str(request["containerName"])
if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,159}", container_name):
    raise ValueError("containerName contains unsupported characters")
result = subprocess.run(["automaton-host-exec", "docker", "stop", container_name], check=False,
                        text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
if result.returncode != 0:
    raise RuntimeError(result.stderr.strip() or f"Host command failed with exit status {result.returncode}")
print(json.dumps({"success": True, "containerName": container_name}, separators=(",", ":")))
