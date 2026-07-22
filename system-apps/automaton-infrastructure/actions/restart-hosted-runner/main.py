#!/usr/bin/env python3
import json
import re
import subprocess
import sys

request = json.load(sys.stdin)
container_name = str(request["containerName"])
if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,159}", container_name):
    raise ValueError("containerName contains unsupported characters")
subprocess.run(["automaton-host-exec", "docker", "restart", container_name], check=True)
print(json.dumps({"success": True, "containerName": container_name}, separators=(",", ":")))
