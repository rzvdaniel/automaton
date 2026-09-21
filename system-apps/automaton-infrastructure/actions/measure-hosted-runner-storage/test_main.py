import importlib.util
import io
import json
import pathlib
import subprocess
import unittest
from contextlib import redirect_stdout
from unittest.mock import patch


MODULE_PATH = pathlib.Path(__file__).with_name("main.py")
SPEC = importlib.util.spec_from_file_location("measure_hosted_runner_storage", MODULE_PATH)
ACTION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ACTION)


class MeasureHostedRunnerStorageTests(unittest.TestCase):
    runner_id = "11111111-1111-1111-1111-111111111111"
    host_id = "22222222-2222-2222-2222-222222222222"

    def request(self):
        compact = self.runner_id.replace("-", "")
        return {
            "operationId": "33333333-3333-3333-3333-333333333333",
            "hostedRunnerId": self.runner_id,
            "runnerHostId": self.host_id,
            "expectedSystemVolumeName": f"automaton-hosted-runner-{compact}-system",
            "expectedUserVolumeName": f"automaton-hosted-runner-{compact}-user",
            "xfsProjectId": 1234567,
        }

    def run_action(self, request, host):
        output = io.StringIO()
        with patch.object(ACTION.sys, "stdin", io.StringIO(json.dumps(request))), \
             patch.object(ACTION, "host", side_effect=host), redirect_stdout(output):
            ACTION.main()
        return json.loads(output.getvalue())

    def test_rejects_arbitrary_volume_name_before_host_access(self):
        request = self.request()
        request["expectedUserVolumeName"] = "someone-elses-volume"
        with patch.object(ACTION.sys, "stdin", io.StringIO(json.dumps(request))), \
             patch.object(ACTION, "host") as host:
            with self.assertRaisesRegex(ValueError, "deterministic"):
                ACTION.main()
            host.assert_not_called()

    def test_rejects_ownership_label_mismatch(self):
        metadata = [{"Labels": {"automaton.managed": "true"}, "Mountpoint": "/var/lib/docker/volumes/x/_data"}]
        with self.assertRaisesRegex(RuntimeError, "ownership labels"):
            def host(*args, **kwargs):
                if args[:3] == ("docker", "info", "--format"):
                    return subprocess.CompletedProcess(args, 0, "/var/lib/docker\n", "")
                return subprocess.CompletedProcess(args, 0, json.dumps(metadata), "")
            self.run_action(self.request(), host)

    def test_measures_only_validated_deterministic_mountpoints(self):
        calls = []

        def host(*args, **kwargs):
            calls.append(args)
            if args[:3] == ("docker", "info", "--format"):
                return subprocess.CompletedProcess(args, 0, "/data/docker\n", "")
            if args[:3] == ("docker", "volume", "inspect"):
                scope = "system" if args[3].endswith("-system") else "user"
                metadata = [{
                    "Labels": {
                        "automaton.managed": "true",
                        "automaton.resource": "hosted-runner-volume",
                        "automaton.hosted-runner-id": self.runner_id,
                        "automaton.storage-scope": scope,
                    },
                    "Mountpoint": f"/data/docker/volumes/{args[3]}/_data",
                }]
                return subprocess.CompletedProcess(args, 0, json.dumps(metadata), "")
            if args[:2] == ("stat", "-f"):
                return subprocess.CompletedProcess(args, 0, "xfs\n", "")
            if args[:2] == ("findmnt", "-no"):
                return subprocess.CompletedProcess(args, 0, "rw,prjquota\n", "")
            if args[:2] == ("xfs_quota", "-x") and "state" in args:
                return subprocess.CompletedProcess(args, 0, "Project quota state: Enforcement\n", "")
            if args[:2] == ("xfs_quota", "-x"):
                return subprocess.CompletedProcess(args, 0, "#1234567 64 0 512\n", "")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = self.run_action(self.request(), host)
        self.assertTrue(result["success"])
        self.assertEqual(result["totalBytes"], result["systemBytes"] + result["userBytes"])
        self.assertEqual(sum(1 for call in calls if call[:3] == ("docker", "volume", "inspect")), 2)
        self.assertEqual(sum(1 for call in calls if call[:2] == ("xfs_quota", "-x")), 3)

    def test_rejects_missing_docker_root(self):
        with patch.object(ACTION.sys, "stdin", io.StringIO(json.dumps(self.request()))), \
             patch.object(ACTION, "host", return_value=subprocess.CompletedProcess([], 1, "", "docker unavailable")):
            with self.assertRaisesRegex(RuntimeError, "Docker data root"):
                ACTION.main()


if __name__ == "__main__":
    unittest.main()
