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
    filesystem_uuid = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"

    def request(self):
        compact = self.runner_id.replace("-", "")
        return {
            "operationId": "33333333-3333-3333-3333-333333333333",
            "hostedRunnerId": self.runner_id,
            "runnerHostId": self.host_id,
            "expectedSystemVolumeName": f"automaton-hosted-runner-{compact}-system",
            "expectedUserVolumeName": f"automaton-hosted-runner-{compact}-user",
            "xfsProjectId": 1234567,
            "storageAllowanceBytes": 512 * 1024 * 1024,
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
            if args[:3] == ("findmnt", "-no", "TARGET"):
                return subprocess.CompletedProcess(args, 0, "/data/docker\n", "")
            if args[:3] == ("findmnt", "-no", "UUID"):
                return subprocess.CompletedProcess(args, 0, self.filesystem_uuid + "\n", "")
            if args[:3] == ("findmnt", "-no", "OPTIONS"):
                return subprocess.CompletedProcess(args, 0, "rw,prjquota\n", "")
            if args[:2] == ("xfs_io", "-c"):
                if args[2] == "lsattr":
                    return subprocess.CompletedProcess(args, 0, f"--------P-------------- {args[3]}\n", "")
                return subprocess.CompletedProcess(args, 0, "fsxattr.projid = 1234567\n", "")
            if args[:2] == ("xfs_quota", "-x") and "state" in args:
                return subprocess.CompletedProcess(
                    args,
                    0,
                    "Project quota state: Enforcement\n",
                    "",
                )
            if args[:2] == ("xfs_quota", "-x"):
                return subprocess.CompletedProcess(args, 0, "#1234567 64 0 524288\n", "")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = self.run_action(self.request(), host)
        self.assertTrue(result["success"])
        self.assertEqual(result["totalBytes"], result["systemBytes"] + result["userBytes"])
        self.assertEqual(result["storageFilesystemUuid"], self.filesystem_uuid)
        self.assertEqual(sum(1 for call in calls if call[:3] == ("docker", "volume", "inspect")), 2)
        self.assertEqual(sum(1 for call in calls if call[:2] == ("xfs_quota", "-x")), 3)

    def test_real_xfs_project_output_is_accepted_and_uses_filesystem_mountpoint(self):
        request = self.request()
        request["xfsProjectId"] = 1165812734
        request["storageAllowanceBytes"] = 2 * 1024 * 1024 * 1024
        calls = []

        def host(*args, **kwargs):
            calls.append(args)
            if args[:3] == ("docker", "info", "--format"):
                return subprocess.CompletedProcess(args, 0, "/srv/automaton-workloads/docker\n", "")
            if args[:3] == ("docker", "volume", "inspect"):
                scope = "system" if args[3].endswith("-system") else "user"
                metadata = [{
                    "Labels": {
                        "automaton.managed": "true",
                        "automaton.resource": "hosted-runner-volume",
                        "automaton.hosted-runner-id": self.runner_id,
                        "automaton.storage-scope": scope,
                    },
                    "Mountpoint": f"/srv/automaton-workloads/docker/volumes/{args[3]}/_data",
                }]
                return subprocess.CompletedProcess(args, 0, json.dumps(metadata), "")
            if args[:2] == ("stat", "-f"):
                return subprocess.CompletedProcess(args, 0, "xfs\n", "")
            if args[:3] == ("findmnt", "-no", "OPTIONS"):
                return subprocess.CompletedProcess(args, 0, "rw,relatime,inode64,prjquota\n", "")
            if args[:3] == ("findmnt", "-no", "TARGET"):
                return subprocess.CompletedProcess(args, 0, "/srv/automaton-workloads\n", "")
            if args[:3] == ("findmnt", "-no", "UUID"):
                return subprocess.CompletedProcess(args, 0, self.filesystem_uuid + "\n", "")
            if args[:2] == ("xfs_io", "-c"):
                if args[2] == "lsattr":
                    return subprocess.CompletedProcess(args, 0, f"--------P-------------- {args[3]}\n", "")
                return subprocess.CompletedProcess(
                    args, 0, f"fsxattr.projid = {request['xfsProjectId']}\n", ""
                )
            if args[:2] == ("xfs_quota", "-x") and "state" in args:
                return subprocess.CompletedProcess(
                    args,
                    0,
                    """User quota state on /srv/automaton-workloads (/dev/sdb)
  Accounting: OFF
  Enforcement: OFF
Project quota state on /srv/automaton-workloads (/dev/sdb)
  Accounting: ON
  Enforcement: ON
""",
                    "",
                )
            if args[:2] == ("xfs_quota", "-x"):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    """Project quota on /srv/automaton-workloads (/dev/sdb)
                               Blocks
Project ID       Used       Soft       Hard    Warn/Grace
---------- --------------------------------------------------
#0              246172          0          0     00 [--------]
#1165812734         36          0    2097152     00 [--------]
""",
                    "",
                )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = self.run_action(request, host)

        self.assertEqual(result["totalBytes"], 36 * 1024)
        self.assertEqual(result["storageFilesystemUuid"], self.filesystem_uuid)
        state_calls = [call for call in calls if call[:2] == ("xfs_quota", "-x") and "state" in call]
        report_calls = [call for call in calls if call[:2] == ("xfs_quota", "-x") and "report -p -b -n" in call]
        self.assertEqual(len(state_calls), 2)
        self.assertEqual(len(report_calls), 1)
        self.assertTrue(all(call[-1] == "/srv/automaton-workloads" for call in state_calls + report_calls))

    def test_rejects_project_assignment_drift_before_reporting_usage(self):
        calls = []
        request = self.request()

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
            if args[:3] == ("findmnt", "-no", "OPTIONS"):
                return subprocess.CompletedProcess(args, 0, "rw,prjquota\n", "")
            if args[:3] == ("findmnt", "-no", "TARGET"):
                return subprocess.CompletedProcess(args, 0, "/data/docker\n", "")
            if args[:3] == ("findmnt", "-no", "UUID"):
                return subprocess.CompletedProcess(args, 0, self.filesystem_uuid + "\n", "")
            if args[:2] == ("xfs_quota", "-x") and "state" in args:
                return subprocess.CompletedProcess(args, 0, "Project quota state: Enforcement\n", "")
            if args[:2] == ("xfs_io", "-c"):
                if args[2] == "lsattr":
                    return subprocess.CompletedProcess(args, 0, f"--------P-------------- {args[3]}\n", "")
                project_id = 77 if args[-1].endswith("-system/_data") else request["xfsProjectId"]
                return subprocess.CompletedProcess(args, 0, f"fsxattr.projid = {project_id}\n", "")
            return subprocess.CompletedProcess(args, 0, "#1234567 64 0 524288\n", "")

        with self.assertRaisesRegex(RuntimeError, "not assigned"):
            self.run_action(request, host)
        self.assertFalse(any(args[:2] == ("xfs_quota", "-x") and "report -p -b -n" in args for args in calls))

    def test_rejects_project_assignment_command_failure(self):
        with patch.object(
            ACTION,
            "host",
            return_value=subprocess.CompletedProcess([], 1, "fsxattr.projid = 1234567", "xfs_io failed"),
        ), self.assertRaisesRegex(RuntimeError, "could not be verified"):
            ACTION.require_project_assignment("/docker/volumes/runner/_data", 1234567)

    def test_rejects_project_assignment_without_inheritance_flag(self):
        def host(*args, **kwargs):
            if args[2] == "stat":
                return subprocess.CompletedProcess(args, 0, "fsxattr.projid = 1234567\n", "")
            return subprocess.CompletedProcess(args, 0, f"------------------------ {args[3]}\n", "")

        with patch.object(ACTION, "host", side_effect=host), self.assertRaisesRegex(RuntimeError, "inheritance"):
            ACTION.require_project_assignment("/docker/volumes/runner/_data", 1234567)

    def test_rejects_hard_limit_drift(self):
        request = self.request()
        request["storageAllowanceBytes"] = 1024

        def host(*args, **kwargs):
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
            if args[:3] == ("findmnt", "-no", "OPTIONS"):
                return subprocess.CompletedProcess(args, 0, "rw,prjquota\n", "")
            if args[:3] == ("findmnt", "-no", "TARGET"):
                return subprocess.CompletedProcess(args, 0, "/data/docker\n", "")
            if args[:3] == ("findmnt", "-no", "UUID"):
                return subprocess.CompletedProcess(args, 0, self.filesystem_uuid + "\n", "")
            if args[:2] == ("xfs_io", "-c"):
                if args[2] == "lsattr":
                    return subprocess.CompletedProcess(args, 0, f"--------P-------------- {args[3]}\n", "")
                return subprocess.CompletedProcess(args, 0, "fsxattr.projid = 1234567\n", "")
            if args[:2] == ("xfs_quota", "-x") and "state" in args:
                return subprocess.CompletedProcess(args, 0, "Project quota state: Enforcement\n", "")
            return subprocess.CompletedProcess(args, 0, "#1234567 64 0 524288\n", "")

        with self.assertRaisesRegex(RuntimeError, "hard limit"):
            self.run_action(request, host)

    def test_rejects_project_quota_enforcement_off(self):
        def host(*args, **kwargs):
            if args[:2] == ("stat", "-f"):
                return subprocess.CompletedProcess(args, 0, "xfs\n", "")
            if args[:3] == ("findmnt", "-no", "OPTIONS"):
                return subprocess.CompletedProcess(args, 0, "rw,prjquota\n", "")
            if args[:3] == ("findmnt", "-no", "TARGET"):
                return subprocess.CompletedProcess(args, 0, "/srv/automaton-workloads\n", "")
            return subprocess.CompletedProcess(args, 0, "Project quota state\n  Accounting: ON\n  Enforcement: OFF\n", "")

        with patch.object(ACTION, "host", side_effect=host), self.assertRaisesRegex(RuntimeError, "enforcement"):
            ACTION.require_xfs_project_quota("/srv/automaton-workloads/docker/volumes/runner/_data")

    def test_rejects_project_quota_accounting_off(self):
        def host(*args, **kwargs):
            if args[:2] == ("stat", "-f"):
                return subprocess.CompletedProcess(args, 0, "xfs\n", "")
            return subprocess.CompletedProcess(args, 0, "rw\n", "")

        with patch.object(ACTION, "host", side_effect=host), self.assertRaisesRegex(RuntimeError, "accounting"):
            ACTION.require_xfs_project_quota("/srv/automaton-workloads/docker/volumes/runner/_data")

    def test_rejects_malformed_project_quota_report(self):
        with self.assertRaisesRegex(RuntimeError, "malformed"):
            ACTION.parse_project_quota_report(
                "Project ID Used Soft Hard\n#1165812734 malformed\n",
                1165812734,
            )

    def test_rejects_missing_docker_root(self):
        with patch.object(ACTION.sys, "stdin", io.StringIO(json.dumps(self.request()))), \
             patch.object(ACTION, "host", return_value=subprocess.CompletedProcess([], 1, "", "docker unavailable")):
            with self.assertRaisesRegex(RuntimeError, "Docker data root"):
                ACTION.main()

    def test_missing_volume_is_a_measurement_failure(self):
        def host(*args, **kwargs):
            if args[:3] == ("docker", "info", "--format"):
                return subprocess.CompletedProcess(args, 0, "/var/lib/docker\n", "")
            if args[:3] == ("docker", "volume", "inspect"):
                return subprocess.CompletedProcess(args, 1, "", "volume not found")
            return subprocess.CompletedProcess(args, 0, "", "")

        with self.assertRaisesRegex(RuntimeError, "volume not found"):
            self.run_action(self.request(), host)


if __name__ == "__main__":
    unittest.main()
