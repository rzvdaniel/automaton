import importlib.util
import io
import json
import os
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).parents[1] / "actions"
RUNNER_ID = "2ca73341-c40b-4e1e-a8b8-586d72236475"
RUNNER_ID_COMPACT = RUNNER_ID.replace("-", "")
SYSTEM_VOLUME = f"automaton-hosted-runner-{RUNNER_ID_COMPACT}-system"
USER_VOLUME = f"automaton-hosted-runner-{RUNNER_ID_COMPACT}-user"
FILESYSTEM_UUID = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"


def load_action(action_key: str):
    path = ROOT / action_key / "main.py"
    spec = importlib.util.spec_from_file_location(f"{action_key}_main", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def volume_labels(scope: str, runner_id: str = RUNNER_ID) -> dict[str, str]:
    return {
        "automaton.managed": "true",
        "automaton.resource": "hosted-runner-volume",
        "automaton.hosted-runner-id": runner_id,
        "automaton.storage-scope": scope,
    }


def action_request() -> dict[str, object]:
    return {
        "operationId": "0f24f98f-0f23-4929-b5d7-cdfe27215aac",
        "hostedRunnerId": RUNNER_ID,
        "userId": "88a0b5a9-3b25-4f1b-aa52-0319744ca451",
        "specificationId": "1e3aeb97-ae73-4ba9-9f28-580f5eeaf6d8",
        "containerName": "automaton-hosted-runner-test",
        "runnerName": "Hosted Runner",
        "image": "automaton/runner:test",
        "networkName": "automaton",
        "systemVolumeName": SYSTEM_VOLUME,
        "userVolumeName": USER_VOLUME,
        "apiUrl": "https://automaton.example",
        "runtimeHostAddress": "10.20.30.40",
        "memoryMb": 384,
        "memoryReservationMb": 256,
        "diskLimitMb": 512,
        "xfsProjectId": 1234567,
        "storageFilesystemUuid": None,
        "pidsLimit": 256,
        "sharedMemoryMb": 64,
        "dockerLogMaxSizeMb": 10,
        "dockerLogMaxFiles": 3,
        "runnerPort": 8080,
        "cpuLimit": 0.5,
    }


class HostedRunnerResourceContractTests(unittest.TestCase):
    def test_read_project_quota_parses_real_host_report_shape_and_units(self):
        report = """Project quota on /srv/automaton-workloads (/dev/sdb)
                               Blocks
Project ID       Used       Soft       Hard    Warn/Grace
---------- --------------------------------------------------
#0              246168          0          0     00 [--------]
#1165812734         28          0          2097152     00 [--------]
"""
        for action_key in (
            "provision-hosted-runner",
            "reprovision-hosted-runner",
            "measure-hosted-runner-storage",
        ):
            with self.subTest(action_key=action_key):
                module = load_action(action_key)
                with patch.object(module, "host", return_value=subprocess.CompletedProcess(
                    ("xfs_quota", "-x"), 0, report, ""
                )) as host_mock:
                    self.assertEqual(
                        module.read_project_quota("/srv/automaton-workloads", 1165812734),
                        (28 * 1024, 2048 * 1024 * 1024),
                    )
                self.assertEqual(host_mock.call_args.args[3], "report -p -b -n")

    def test_read_project_quota_returns_none_for_an_absent_project_row(self):
        report = """Project quota on /srv/automaton-workloads (/dev/sdb)
Project ID       Used       Soft       Hard    Warn/Grace
---------- --------------------------------------------------
#0              246168          0          0     00 [--------]
"""
        for action_key in (
            "provision-hosted-runner",
            "reprovision-hosted-runner",
            "measure-hosted-runner-storage",
        ):
            with self.subTest(action_key=action_key):
                module = load_action(action_key)
                with patch.object(module, "host", return_value=subprocess.CompletedProcess(
                    ("xfs_quota", "-x"), 0, report, ""
                )):
                    self.assertIsNone(module.read_project_quota("/srv/automaton-workloads", 1165812734))

    def test_project_quota_enforcement_requires_on_for_all_actions(self):
        enabled = """Project quota state on /srv/automaton-workloads (/dev/sdb)
  Accounting: ON
  Enforcement: ON
"""
        disabled = """Project quota state on /srv/automaton-workloads (/dev/sdb)
  Accounting: ON
  Enforcement: OFF
"""
        for action_key in (
            "provision-hosted-runner",
            "reprovision-hosted-runner",
            "measure-hosted-runner-storage",
        ):
            with self.subTest(action_key=action_key):
                module = load_action(action_key)
                self.assertTrue(module.project_quota_enforcement_enabled(enabled))
                self.assertFalse(module.project_quota_enforcement_enabled(disabled))

    def test_read_project_quota_rejects_malformed_target_row(self):
        module = load_action("provision-hosted-runner")
        report = "Project ID Used Soft Hard\n#1165812734 malformed\n"
        with patch.object(module, "host", return_value=subprocess.CompletedProcess(
            ("xfs_quota", "-x"), 0, report, ""
        )):
            with self.assertRaisesRegex(RuntimeError, "malformed"):
                module.read_project_quota("/srv/automaton-workloads", 1165812734)

    def test_read_project_quota_propagates_nonzero_xfs_command_failure(self):
        module = load_action("provision-hosted-runner")
        with patch.object(module, "host", side_effect=RuntimeError("xfs_quota failed")):
            with self.assertRaisesRegex(RuntimeError, "xfs_quota failed"):
                module.read_project_quota("/srv/automaton-workloads", 1165812734)

    def test_first_time_project_is_not_configured_before_limit_then_is_verified(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        calls, _ = self.run_provision_action(
            "provision-hosted-runner",
            existing,
            request_override={"diskLimitMb": 2048, "xfsProjectId": 1165812734},
        )
        report_indices = [
            index for index, call in enumerate(calls)
            if call[:2] == ("xfs_quota", "-x")
            and any("report -p -b -n" in argument for argument in call)
        ]
        self.assertGreaterEqual(len(report_indices), 3)
        limit_index = next(
            index for index, call in enumerate(calls)
            if call[:2] == ("xfs_quota", "-x")
            and any("limit -p bhard=2048m 1165812734" in argument for argument in call)
        )
        self.assertLess(report_indices[0], limit_index)
        self.assertGreater(report_indices[1], limit_index)

    def run_provision_action(
        self,
        action_key: str,
        existing_volumes: dict[str, dict[str, str]] | None = None,
        other_runner_volumes: dict[str, dict[str, str]] | None = None,
        missing_volume_error: str = "No such volume: {name}",
        image_pull_error: str | None = None,
        calls_out: list[tuple[str, ...]] | None = None,
        request_override: dict[str, object] | None = None,
        existing_project_quota_limit_mb: int | None = None,
        other_runner_project_id: int | None = None,
        xfs_project_error: str | None = None,
        xfs_limit_error: str | None = None,
        xfs_assignment_succeeds: bool = True,
        xfs_quota_verification_succeeds: bool = True,
        final_quota_hard_limit_matches: bool = True,
        reset_storage_after_container: bool = False,
        result_out: list[dict[str, object]] | None = None,
    ):
        module = load_action(action_key)
        calls = calls_out if calls_out is not None else []
        volumes = dict(existing_volumes or {})
        volumes.update(other_runner_volumes or {})
        request = action_request()
        if request_override:
            request.update(request_override)
        assigned_project = False
        quota_configured = False
        container_replaced = False

        def fake_host(*arguments, check=True):
            nonlocal assigned_project, quota_configured, container_replaced
            calls.append(arguments)
            if arguments[:2] == ("docker", "pull") and image_pull_error is not None:
                raise RuntimeError(image_pull_error)
            if arguments[:3] == ("docker", "volume", "inspect"):
                name = arguments[3]
                if name not in volumes:
                    return subprocess.CompletedProcess(
                        arguments,
                        1,
                        "",
                        missing_volume_error.format(name=name),
                    )
                return subprocess.CompletedProcess(
                    arguments,
                    0,
                    json.dumps([{
                        "Name": name,
                        "Labels": volumes[name],
                        "Mountpoint": f"/var/lib/docker/volumes/{name}/_data",
                    }]),
                    "",
                )
            if arguments[:3] == ("docker", "volume", "ls"):
                return subprocess.CompletedProcess(arguments, 0, "\n".join(volumes) + "\n", "")
            if arguments[:3] == ("docker", "volume", "create"):
                name = arguments[-1]
                labels = {}
                for index, argument in enumerate(arguments):
                    if argument == "--label":
                        key, value = arguments[index + 1].split("=", 1)
                        labels[key] = value
                volumes[name] = labels
                return subprocess.CompletedProcess(arguments, 0, f"{name}\n", "")
            if arguments[:3] == ("docker", "info", "--format"):
                stdout = "2|systemd\n" if "CgroupVersion" in arguments[3] else "/var/lib/docker\n"
            elif arguments[:2] == ("stat", "-f"):
                stdout = "xfs\n"
            elif arguments[:2] == ("findmnt", "-no") and arguments[2] == "OPTIONS":
                stdout = "rw,prjquota\n"
            elif arguments[:2] == ("findmnt", "-no") and arguments[2] == "TARGET":
                stdout = "/var/lib/docker\n"
            elif arguments[:2] == ("findmnt", "-no") and arguments[2] == "UUID":
                stdout = FILESYSTEM_UUID + "\n"
            elif arguments[:2] == ("xfs_quota", "-x") and "state" in arguments:
                stdout = "Project quota state: Enforcement\n"
            elif arguments[:2] == ("xfs_quota", "-x") and any("project -s" in argument for argument in arguments):
                if xfs_project_error is not None:
                    raise RuntimeError(xfs_project_error)
                assigned_project = True
                stdout = ""
            elif arguments[:2] == ("xfs_quota", "-x") and any("limit -p" in argument for argument in arguments):
                if xfs_limit_error is not None:
                    raise RuntimeError(xfs_limit_error)
                quota_configured = True
                stdout = ""
            elif arguments[:2] == ("xfs_quota", "-x") and any("report -p" in argument for argument in arguments):
                project_id = int(request["xfsProjectId"])
                if not xfs_quota_verification_succeeds:
                    stdout = "quota output unavailable\n"
                elif not quota_configured and existing_project_quota_limit_mb is not None:
                    hard_blocks = existing_project_quota_limit_mb * 1024
                    stdout = f"Project ID   Used   Soft   Hard\n#{project_id} 0 0 {hard_blocks}\n"
                elif not quota_configured:
                    stdout = "Project quota on /var/lib/docker\n#0 0 0 0\n"
                else:
                    hard_blocks = int(request["diskLimitMb"]) * 1024
                    if container_replaced and not final_quota_hard_limit_matches:
                        hard_blocks = 0
                    stdout = f"Project ID   Used   Soft   Hard\n#{project_id} 0 0 {hard_blocks}\n"
            elif arguments[:2] == ("xfs_io", "-c"):
                mountpoint = arguments[3]
                is_other_runner_volume = any(
                    mountpoint == f"/var/lib/docker/volumes/{name}/_data"
                    for name in (other_runner_volumes or {})
                )
                if is_other_runner_volume:
                    project_id = other_runner_project_id or 0
                else:
                    project_id = int(request["xfsProjectId"]) if assigned_project and xfs_assignment_succeeds else 0
                stdout = f"fsxattr.projid = {project_id}\n"
            elif arguments[:2] == ("systemctl", "show"):
                stdout = "1\n1\n256\n"
            else:
                if arguments[:2] == ("docker", "run") and reset_storage_after_container:
                    assigned_project = False
                    quota_configured = False
                if arguments[:2] == ("docker", "run"):
                    container_replaced = True
                stdout = "container-id\n" if arguments[:2] == ("docker", "run") else ""
            return subprocess.CompletedProcess(arguments, 0, stdout, "")

        action_output = io.StringIO()
        with (
            patch.object(module, "host", side_effect=fake_host),
            patch.dict(os.environ, {"AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN": "re_test"}),
            patch.object(sys, "stdin", io.StringIO(json.dumps(request))),
            patch.object(sys, "stdout", action_output),
        ):
            module.main()

        if result_out is not None:
            result_out.append(json.loads(action_output.getvalue()))

        return calls, volumes

    def test_provisioning_reports_and_preserves_the_verified_filesystem_uuid(self):
        output: list[dict[str, object]] = []
        self.run_provision_action("provision-hosted-runner", result_out=output)

        self.assertEqual(output[0]["storageFilesystemUuid"], FILESYSTEM_UUID)

    def test_reprovision_rejects_a_different_filesystem_before_replacing_container(self):
        calls: list[tuple[str, ...]] = []
        with self.assertRaisesRegex(RuntimeError, "different XFS filesystem"):
            self.run_provision_action(
                "reprovision-hosted-runner",
                calls_out=calls,
                request_override={"storageFilesystemUuid": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"},
            )

        self.assertFalse(any(call[:2] == ("docker", "rm") for call in calls))
        self.assertFalse(any(call[:2] == ("docker", "run") for call in calls))

    def test_different_snapshot_cannot_overwrite_a_shared_project_quota(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        other_runner_id = "88888888-8888-4888-8888-888888888888"
        other_runner_compact_id = other_runner_id.replace("-", "")
        other_volumes = {
            f"automaton-hosted-runner-{other_runner_compact_id}-system": volume_labels("system", other_runner_id),
            f"automaton-hosted-runner-{other_runner_compact_id}-user": volume_labels("user", other_runner_id),
        }

        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                calls: list[tuple[str, ...]] = []
                with self.assertRaisesRegex(RuntimeError, "used by another Hosted Runner"):
                    self.run_provision_action(
                        action_key,
                        existing,
                        other_runner_volumes=other_volumes,
                        calls_out=calls,
                        request_override={"diskLimitMb": 512, "xfsProjectId": 1234567},
                        existing_project_quota_limit_mb=2048,
                        other_runner_project_id=1234567,
                    )

                self.assertFalse(any(call[:2] == ("docker", "rm") for call in calls))
                self.assertFalse(any(call[:2] == ("docker", "run") for call in calls))
                self.assertFalse(any(
                    call[:2] == ("xfs_quota", "-x") and any("limit -p" in arg for arg in call)
                    for call in calls
                ))

    def test_same_snapshot_can_reuse_a_shared_project_quota(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        other_runner_id = "88888888-8888-4888-8888-888888888888"
        other_runner_compact_id = other_runner_id.replace("-", "")
        other_volumes = {
            f"automaton-hosted-runner-{other_runner_compact_id}-system": volume_labels("system", other_runner_id),
            f"automaton-hosted-runner-{other_runner_compact_id}-user": volume_labels("user", other_runner_id),
        }

        calls, _ = self.run_provision_action(
            "provision-hosted-runner",
            existing,
            other_runner_volumes=other_volumes,
            existing_project_quota_limit_mb=512,
            other_runner_project_id=1234567,
        )

        self.assertTrue(any(
            call[:2] == ("xfs_quota", "-x") and any("limit -p bhard=512m 1234567" in arg for arg in call)
            for call in calls
        ))
        self.assertFalse(any(call[:3] == ("docker", "volume", "ls") for call in calls))

    def assert_resource_contract(self, calls):
        run = next(call for call in calls if call[:2] == ("docker", "run"))
        expected_pairs = {
            "--shm-size": "64m",
            "--security-opt": "no-new-privileges=true",
            "--log-driver": "local",
        }
        for option, value in expected_pairs.items():
            index = run.index(option)
            self.assertEqual(run[index + 1], value)
        self.assertIn("max-size=10m", run)
        self.assertIn("max-file=3", run)
        self.assertNotIn("--privileged", run)
        self.assertIn("--cgroup-parent", run)
        self.assertIn(f"automaton-hosted-runner-{RUNNER_ID_COMPACT}.slice", run)
        self.assertNotIn("--cpus", run)
        self.assertNotIn("--memory", run)
        self.assertNotIn("--pids-limit", run)
        self.assertNotIn("--pid", run)
        self.assertFalse("--pid=host" in run or "host" in run[run.index("--network") + 1:run.index("--network") + 2])
        self.assertNotIn("/var/run/docker.sock", run)
        self.assertFalse(any(
            call[:2] == ("docker", "run") and any(
                argument.startswith("/host:") or argument.startswith("/var/run/docker.sock:")
                for argument in call
            )
            for call in calls
        ))
        network_index = run.index("--network")
        self.assertEqual(run[network_index + 1], "automaton")
        self.assertNotEqual(run[network_index + 1], "host")
        self.assertNotIn("-p", run)
        self.assertIn("automaton.managed=true", run)
        self.assertIn("automaton.resource=hosted-runner", run)
        self.assertIn(f"{SYSTEM_VOLUME}:/automaton/system", run)
        self.assertIn(f"{USER_VOLUME}:/automaton/storage", run)
        self.assertIn("AUTOMATON_RUNTIME_HOST_ADDRESS=10.20.30.40", run)
        self.assertIn("AUTOMATON_RUNNER_PURPOSE=Managed", run)
        self.assertIn("AUTOMATON_RUNNER_APPLICATION_PROFILE=Standard", run)
        self.assertNotIn(f"{USER_VOLUME}:/automaton/workspace", run)
        self.assertNotIn(f"{USER_VOLUME}:/app/logs", run)

    def test_fresh_volumes_are_created_labeled_inspected_and_mounted(self):
        calls, volumes = self.run_provision_action("provision-hosted-runner")

        self.assertEqual(
            sum(call[:3] == ("docker", "volume", "create") for call in calls),
            2,
        )
        self.assertEqual(volumes[SYSTEM_VOLUME], volume_labels("system"))
        self.assertEqual(volumes[USER_VOLUME], volume_labels("user"))
        self.assertGreaterEqual(
            sum(call[:3] == ("docker", "volume", "inspect") for call in calls),
            4,
        )
        first_assignment = next(
            index for index, call in enumerate(calls)
            if call[:2] == ("xfs_quota", "-x") and any("project -s" in argument for argument in call)
        )
        last_volume_create = max(
            index for index, call in enumerate(calls) if call[:3] == ("docker", "volume", "create")
        )
        self.assertLess(last_volume_create, first_assignment)
        self.assert_resource_contract(calls)

    def test_xfs_assignment_uses_numeric_project_and_filesystem_mount_for_both_actions(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                calls, _ = self.run_provision_action(
                    action_key,
                    existing,
                    request_override={"diskLimitMb": 2048, "xfsProjectId": 1165812734},
                )
                project_calls = [
                    call for call in calls
                    if call[:2] == ("xfs_quota", "-x")
                    and any("project -s" in argument for argument in call)
                ]
                self.assertEqual(len(project_calls), 2)
                for call in project_calls:
                    self.assertIn("project -s -p ", call[3])
                    self.assertIn(" 1165812734", call[3])
                    self.assertNotIn("#1165812734", call[3])
                    self.assertEqual(call[-1], "/var/lib/docker")
                self.assertCountEqual(
                    [call[3].split(" -p ", 1)[1].rsplit(" ", 1)[0] for call in project_calls],
                    [
                        f"/var/lib/docker/volumes/{SYSTEM_VOLUME}/_data",
                        f"/var/lib/docker/volumes/{USER_VOLUME}/_data",
                    ],
                )
                limit_call = next(
                    call for call in calls
                    if call[:2] == ("xfs_quota", "-x")
                    and any("limit -p" in argument for argument in call)
                )
                self.assertEqual(limit_call[3], "limit -p bhard=2048m 1165812734")
                self.assertEqual(limit_call[-1], "/var/lib/docker")

    def test_xfs_project_command_failure_is_fatal_before_container_replacement(self):
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                existing = {
                    SYSTEM_VOLUME: volume_labels("system"),
                    USER_VOLUME: volume_labels("user"),
                }
                calls: list[tuple[str, ...]] = []
                with self.assertRaisesRegex(RuntimeError, "xfs setup failed"):
                    self.run_provision_action(
                        action_key,
                        existing,
                        image_pull_error=None,
                        calls_out=calls,
                        xfs_project_error="xfs setup failed",
                    )
                self.assertFalse(any(call[:3] == ("docker", "rm", "-f") for call in calls))
                self.assertFalse(any(call[:2] == ("docker", "run") for call in calls))

    def test_xfs_assignment_verification_failure_is_fatal_before_container_replacement(self):
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                existing = {
                    SYSTEM_VOLUME: volume_labels("system"),
                    USER_VOLUME: volume_labels("user"),
                }
                calls: list[tuple[str, ...]] = []
                with self.assertRaisesRegex(RuntimeError, "was not assigned"):
                    self.run_provision_action(
                        action_key,
                        existing,
                        calls_out=calls,
                        xfs_assignment_succeeds=False,
                    )
                self.assertFalse(any(call[:3] == ("docker", "rm", "-f") for call in calls))
                self.assertFalse(any(call[:2] == ("docker", "run") for call in calls))

    def test_xfs_quota_verification_failure_is_fatal_before_container_replacement(self):
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                existing = {
                    SYSTEM_VOLUME: volume_labels("system"),
                    USER_VOLUME: volume_labels("user"),
                }
                calls: list[tuple[str, ...]] = []
                with self.assertRaisesRegex(RuntimeError, "quota for project"):
                    self.run_provision_action(
                        action_key,
                        existing,
                        calls_out=calls,
                        xfs_quota_verification_succeeds=False,
                    )
                self.assertFalse(any(call[:3] == ("docker", "rm", "-f") for call in calls))
                self.assertFalse(any(call[:2] == ("docker", "run") for call in calls))

    def test_xfs_quota_set_failure_is_fatal_before_container_replacement(self):
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                existing = {
                    SYSTEM_VOLUME: volume_labels("system"),
                    USER_VOLUME: volume_labels("user"),
                }
                calls: list[tuple[str, ...]] = []
                with self.assertRaisesRegex(RuntimeError, "quota setup failed"):
                    self.run_provision_action(
                        action_key,
                        existing,
                        calls_out=calls,
                        xfs_limit_error="quota setup failed",
                    )
                self.assertFalse(any(call[:3] == ("docker", "rm", "-f") for call in calls))
                self.assertFalse(any(call[:2] == ("docker", "run") for call in calls))

    def test_final_storage_postconditions_are_verified_after_container_replacement(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                calls: list[tuple[str, ...]] = []
                with self.assertRaisesRegex(RuntimeError, "was not assigned to final managed volume"):
                    self.run_provision_action(
                        action_key,
                        existing,
                        calls_out=calls,
                        reset_storage_after_container=True,
                    )
                run_index = next(index for index, call in enumerate(calls) if call[:2] == ("docker", "run"))
                final_project_check = next(
                    index for index, call in enumerate(calls[run_index + 1:], run_index + 1)
                    if call[:2] == ("xfs_io", "-c")
                )
                self.assertGreater(final_project_check, run_index)
                final_project_checks = [
                    index for index, call in enumerate(calls[run_index + 1:], run_index + 1)
                    if call[:2] == ("xfs_io", "-c")
                ]
                self.assertEqual(len(final_project_checks), 1)

    def test_successful_final_storage_postconditions_check_both_roots(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                calls, _ = self.run_provision_action(action_key, existing)
                run_index = next(index for index, call in enumerate(calls) if call[:2] == ("docker", "run"))
                final_project_checks = [
                    index for index, call in enumerate(calls[run_index + 1:], run_index + 1)
                    if call[:2] == ("xfs_io", "-c")
                ]
                self.assertEqual(len(final_project_checks), 2)

    def test_final_quota_readback_mismatch_fails_after_container_replacement(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                calls: list[tuple[str, ...]] = []
                with self.assertRaisesRegex(RuntimeError, "hard limit did not match diskLimitMb on final volumes"):
                    self.run_provision_action(
                        action_key,
                        existing,
                        calls_out=calls,
                        final_quota_hard_limit_matches=False,
                    )
                run_index = next(index for index, call in enumerate(calls) if call[:2] == ("docker", "run"))
                self.assertTrue(any(index > run_index for index, call in enumerate(calls) if call[:2] == ("xfs_quota", "-x") and any("report -p" in argument for argument in call)))

    def test_both_hosted_runner_actions_preserve_the_tenant_safe_contract(self):
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                calls, _ = self.run_provision_action(action_key)
                self.assert_resource_contract(calls)

    def test_missing_volume_responses_are_recognized_case_insensitively_by_both_actions(self):
        missing_volume_errors = [
            "No such volume: {name}",
            "Error response from daemon: get {name}: no such volume",
            "ERROR RESPONSE FROM DAEMON: GET {name}: NO SUCH VOLUME",
        ]
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            for missing_volume_error in missing_volume_errors:
                with self.subTest(action_key=action_key, missing_volume_error=missing_volume_error):
                    calls, volumes = self.run_provision_action(
                        action_key,
                        missing_volume_error=missing_volume_error,
                    )
                    self.assertEqual(volumes[SYSTEM_VOLUME], volume_labels("system"))
                    self.assertEqual(volumes[USER_VOLUME], volume_labels("user"))
                    self.assertEqual(
                        sum(call[:3] == ("docker", "volume", "create") for call in calls),
                        2,
                    )

    def test_unrelated_volume_inspection_error_remains_fatal(self):
        with self.assertRaisesRegex(RuntimeError, "permission denied"):
            self.run_provision_action(
                "provision-hosted-runner",
                missing_volume_error="Error response from daemon: permission denied",
            )

    def test_valid_existing_volumes_are_reused_without_recreation(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        calls, _ = self.run_provision_action("reprovision-hosted-runner", existing)

        self.assertFalse(any(call[:3] == ("docker", "volume", "create") for call in calls))
        self.assert_resource_contract(calls)

    def test_image_is_refreshed_before_container_replacement_for_both_actions(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                calls, _ = self.run_provision_action(action_key, existing)
                pull_index = next(index for index, call in enumerate(calls) if call[:2] == ("docker", "pull"))
                remove_index = next(index for index, call in enumerate(calls) if call[:3] == ("docker", "rm", "-f"))
                run_index = next(index for index, call in enumerate(calls) if call[:2] == ("docker", "run"))
                self.assertEqual(calls[pull_index], ("docker", "pull", "automaton/runner:test"))
                self.assertLess(pull_index, remove_index)
                self.assertLess(pull_index, run_index)

    def test_image_pull_failure_preserves_existing_container_for_both_actions(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                calls: list[tuple[str, ...]] = []
                with self.assertRaisesRegex(RuntimeError, "registry unavailable"):
                    self.run_provision_action(
                        action_key,
                        existing,
                        image_pull_error="registry unavailable",
                        calls_out=calls,
                    )
                self.assertEqual(calls[-1], ("docker", "pull", "automaton/runner:test"))
                self.assertFalse(any(call[:3] == ("docker", "rm", "-f") for call in calls))
                self.assertFalse(any(call[:2] == ("docker", "run") for call in calls))

    def test_quota_decrease_below_current_project_usage_fails_before_container_replacement(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        module = load_action("provision-hosted-runner")
        calls = []
        assigned_project = False

        def fake_host(*arguments, check=True):
            nonlocal assigned_project
            calls.append(arguments)
            if arguments[:3] == ("docker", "volume", "inspect"):
                name = arguments[3]
                return subprocess.CompletedProcess(arguments, 0, json.dumps([{
                    "Labels": existing[name],
                    "Mountpoint": f"/var/lib/docker/volumes/{name}/_data",
                }]), "")
            if arguments[:3] == ("docker", "info", "--format"):
                stdout = "2|systemd\n" if "CgroupVersion" in arguments[3] else "/var/lib/docker\n"
                return subprocess.CompletedProcess(arguments, 0, stdout, "")
            if arguments[:2] == ("stat", "-f"):
                return subprocess.CompletedProcess(arguments, 0, "xfs\n", "")
            if arguments[:2] == ("findmnt", "-no") and arguments[2] == "OPTIONS":
                return subprocess.CompletedProcess(arguments, 0, "rw,prjquota\n", "")
            if arguments[:2] == ("findmnt", "-no") and arguments[2] == "TARGET":
                return subprocess.CompletedProcess(arguments, 0, "/var/lib/docker\n", "")
            if arguments[:2] == ("findmnt", "-no") and arguments[2] == "UUID":
                return subprocess.CompletedProcess(arguments, 0, FILESYSTEM_UUID + "\n", "")
            if arguments[:2] == ("xfs_quota", "-x") and "state" in arguments:
                return subprocess.CompletedProcess(arguments, 0, "Project quota state: Enforcement\n", "")
            if arguments[:2] == ("xfs_quota", "-x") and any("project -s" in argument for argument in arguments):
                assigned_project = True
                return subprocess.CompletedProcess(arguments, 0, "", "")
            if arguments[:2] == ("xfs_quota", "-x") and any("report -p" in argument for argument in arguments):
                return subprocess.CompletedProcess(arguments, 0, "#1234567 600000 0 0\n", "")
            if arguments[:2] == ("xfs_io", "-c"):
                project_id = 1234567 if assigned_project else 0
                return subprocess.CompletedProcess(arguments, 0, f"fsxattr.projid = {project_id}\n", "")
            return subprocess.CompletedProcess(arguments, 0, "", "")

        with (
            patch.object(module, "host", side_effect=fake_host),
            patch.dict(os.environ, {"AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN": "re_test"}),
            patch.object(sys, "stdin", io.StringIO(json.dumps(action_request()))),
            patch.object(sys, "stdout", io.StringIO()),
            self.assertRaisesRegex(RuntimeError, "below current XFS project usage"),
        ):
            module.main()
        self.assertFalse(any(call[:3] == ("docker", "rm", "-f") for call in calls))
        self.assertFalse(any(call[:2] == ("docker", "run") for call in calls))

    def test_unsupported_cgroup_host_fails_closed_before_container_replacement(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        for action_key in ("provision-hosted-runner", "reprovision-hosted-runner"):
            with self.subTest(action_key=action_key):
                module = load_action(action_key)
                calls = []
                assigned_project = False

                def fake_host(*arguments, check=True):
                    nonlocal assigned_project
                    calls.append(arguments)
                    if arguments[:3] == ("docker", "volume", "inspect"):
                        name = arguments[3]
                        return subprocess.CompletedProcess(arguments, 0, json.dumps([{
                            "Labels": existing[name],
                            "Mountpoint": f"/var/lib/docker/volumes/{name}/_data",
                        }]), "")
                    if arguments[:3] == ("docker", "info", "--format"):
                        stdout = "1|cgroupfs\n" if "CgroupVersion" in arguments[3] else "/var/lib/docker\n"
                        return subprocess.CompletedProcess(arguments, 0, stdout, "")
                    if arguments[:2] == ("stat", "-f"):
                        return subprocess.CompletedProcess(arguments, 0, "xfs\n", "")
                    if arguments[:2] == ("findmnt", "-no") and arguments[2] == "OPTIONS":
                        return subprocess.CompletedProcess(arguments, 0, "rw,prjquota\n", "")
                    if arguments[:2] == ("findmnt", "-no") and arguments[2] == "TARGET":
                        return subprocess.CompletedProcess(arguments, 0, "/var/lib/docker\n", "")
                    if arguments[:2] == ("findmnt", "-no") and arguments[2] == "UUID":
                        return subprocess.CompletedProcess(arguments, 0, FILESYSTEM_UUID + "\n", "")
                    if arguments[:2] == ("xfs_quota", "-x") and "state" in arguments:
                        return subprocess.CompletedProcess(arguments, 0, "Project quota state: Enforcement\n", "")
                    if arguments[:2] == ("xfs_quota", "-x") and any("project -s" in argument for argument in arguments):
                        assigned_project = True
                        return subprocess.CompletedProcess(arguments, 0, "", "")
                    if arguments[:2] == ("xfs_quota", "-x") and any("report -p" in argument for argument in arguments):
                        return subprocess.CompletedProcess(arguments, 0, "#1234567 0 0 524288\n", "")
                    if arguments[:2] == ("xfs_io", "-c"):
                        project_id = 1234567 if assigned_project else 0
                        return subprocess.CompletedProcess(arguments, 0, f"fsxattr.projid = {project_id}\n", "")
                    return subprocess.CompletedProcess(arguments, 0, "", "")

                with (
                    patch.object(module, "host", side_effect=fake_host),
                    patch.dict(os.environ, {"AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN": "re_test"}),
                    patch.object(sys, "stdin", io.StringIO(json.dumps(action_request()))),
                    patch.object(sys, "stdout", io.StringIO()),
                    self.assertRaisesRegex(RuntimeError, "cgroup v2 with Docker systemd cgroups"),
                ):
                    module.main()
                self.assertFalse(any(call[:3] == ("docker", "rm", "-f") for call in calls))
                self.assertFalse(any(call[:2] == ("docker", "run") for call in calls))

    def test_existing_volume_with_missing_or_conflicting_labels_is_rejected(self):
        invalid_labels = [
            {},
            volume_labels("system", "88888888-8888-4888-8888-888888888888"),
            {**volume_labels("system"), "automaton.storage-scope": "user"},
        ]
        for labels in invalid_labels:
            with self.subTest(labels=labels):
                existing = {
                    SYSTEM_VOLUME: labels,
                    USER_VOLUME: volume_labels("user"),
                }
                with self.assertRaisesRegex(RuntimeError, "ownership labels"):
                    self.run_provision_action("provision-hosted-runner", existing)

    def run_delete_action(
        self,
        preserve_volumes: object,
        labels_by_volume: dict[str, dict[str, str]],
    ):
        module = load_action("delete-hosted-runner")
        calls = []
        request = {
            "operationId": "0f24f98f-0f23-4929-b5d7-cdfe27215aac",
            "hostedRunnerId": RUNNER_ID,
            "containerName": "automaton-hosted-runner-test",
            "preserveVolumes": preserve_volumes,
            "systemVolumeName": SYSTEM_VOLUME,
            "userVolumeName": USER_VOLUME,
        }

        def fake_host(*arguments):
            calls.append(arguments)
            if arguments[:3] == ("docker", "volume", "inspect"):
                name = arguments[3]
                labels = labels_by_volume.get(name)
                if labels is None:
                    return subprocess.CompletedProcess(arguments, 1, "", f"No such volume: {name}")
                return subprocess.CompletedProcess(
                    arguments,
                    0,
                    json.dumps([{"Name": name, "Labels": labels}]),
                    "",
                )
            if arguments[:3] == ("docker", "container", "inspect"):
                return subprocess.CompletedProcess(arguments, 1, "", "No such container")
            if arguments[:4] == ("docker", "container", "ls", "--all"):
                return subprocess.CompletedProcess(arguments, 0, "", "")
            if arguments[:3] == ("docker", "volume", "rm"):
                return subprocess.CompletedProcess(arguments, 0, arguments[3], "")
            return subprocess.CompletedProcess(arguments, 0, "", "")

        with (
            patch.object(module, "host", side_effect=fake_host),
            patch.object(sys, "stdin", io.StringIO(json.dumps(request))),
            patch.object(sys, "stdout", io.StringIO()),
        ):
            module.main()
        return calls

    def test_delete_preserve_keeps_both_volumes(self):
        calls = self.run_delete_action(True, {})
        self.assertFalse(any(call[:3] == ("docker", "volume", "rm") for call in calls))

    def test_delete_data_inspects_and_removes_both_owned_volumes(self):
        calls = self.run_delete_action(
            False,
            {
                SYSTEM_VOLUME: volume_labels("system"),
                USER_VOLUME: volume_labels("user"),
            },
        )
        self.assertEqual(sum(call[:3] == ("docker", "volume", "inspect") for call in calls), 4)
        self.assertEqual(sum(call[:3] == ("docker", "volume", "rm") for call in calls), 2)
        inspection_indexes = [
            index
            for index, call in enumerate(calls)
            if call[:3] == ("docker", "volume", "inspect")
        ]
        mutation_indexes = [
            index
            for index, call in enumerate(calls)
            if call[:3] in (("docker", "rm", "-f"), ("docker", "volume", "rm"))
        ]
        self.assertEqual(sum(index < min(mutation_indexes) for index in inspection_indexes), 3)

    def test_delete_refuses_unlabeled_or_mismatched_volume(self):
        invalid_labels = [
            {},
            volume_labels("user", "88888888-8888-4888-8888-888888888888"),
            {**volume_labels("user"), "automaton.storage-scope": "system"},
        ]
        for labels in invalid_labels:
            with self.subTest(labels=labels):
                module = load_action("delete-hosted-runner")
                calls = []
                request = {
                    "operationId": "0f24f98f-0f23-4929-b5d7-cdfe27215aac",
                    "hostedRunnerId": RUNNER_ID,
                    "containerName": "automaton-hosted-runner-test",
                    "preserveVolumes": False,
                    "systemVolumeName": SYSTEM_VOLUME,
                    "userVolumeName": USER_VOLUME,
                }
                labels_by_volume = {
                    SYSTEM_VOLUME: volume_labels("system"),
                    USER_VOLUME: labels,
                }

                def fake_host(*arguments):
                    calls.append(arguments)
                    if arguments[:3] == ("docker", "volume", "inspect"):
                        name = arguments[3]
                        return subprocess.CompletedProcess(
                            arguments,
                            0,
                            json.dumps([{"Name": name, "Labels": labels_by_volume[name]}]),
                            "",
                        )
                    return subprocess.CompletedProcess(arguments, 0, "", "")

                with (
                    patch.object(module, "host", side_effect=fake_host),
                    patch.object(sys, "stdin", io.StringIO(json.dumps(request))),
                    patch.object(sys, "stdout", io.StringIO()),
                    self.assertRaisesRegex(RuntimeError, "ownership labels"),
                ):
                    module.main()

                self.assertFalse(any(call[:3] == ("docker", "volume", "rm") for call in calls))
                self.assertFalse(any(call[:3] == ("docker", "rm", "-f") for call in calls))

    def test_delete_requires_boolean_volume_disposition(self):
        with self.assertRaisesRegex(ValueError, "explicitly"):
            self.run_delete_action("preserve", {})


if __name__ == "__main__":
    unittest.main()
