import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


class HostValidationActionTests(unittest.TestCase):
    def test_probe_returns_structured_snapshot_without_real_host_commands(self):
        with tempfile.TemporaryDirectory() as directory:
            helper = Path(directory) / "automaton-host-exec"
            helper.write_text("""#!/usr/bin/env bash
case "$3" in
  *nproc*) printf '2' ;;
  *'free -m'*) printf '4096' ;;
  *'df -Pm'*) printf '40960' ;;
  *'docker info >'*) exit 0 ;;
  *'docker network inspect'*) exit 0 ;;
  *'docker info --format'*) printf 'overlay2' ;;
  *hostname*) printf 'test-host' ;;
  *) printf 'test-value' ;;
esac
""")
            helper.chmod(0o755)
            result = subprocess.run(
                ["bash", str(Path(__file__).with_name("main.sh"))],
                input=json.dumps({"dockerNetwork": "test-network; never-execute"}),
                text=True, capture_output=True, check=True,
                env={**os.environ, "PATH": directory + os.pathsep + os.environ["PATH"]},
            )
            snapshot = json.loads(result.stdout)
            self.assertEqual(snapshot["totalCpuCores"], 2)
            self.assertEqual(snapshot["totalMemoryMb"], 4096)
            self.assertEqual(snapshot["totalDiskMb"], 40960)
            self.assertEqual(snapshot["metadata"]["hostname"], "test-host")
            self.assertEqual(snapshot["health"]["docker"], "Healthy")
            self.assertEqual(snapshot["health"]["network"], "Healthy")
            self.assertEqual(snapshot["health"]["disk"], "Healthy")


if __name__ == "__main__":
    unittest.main()
