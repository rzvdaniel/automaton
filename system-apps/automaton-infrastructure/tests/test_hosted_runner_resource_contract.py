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
        "memoryMb": 384,
        "memoryReservationMb": 256,
        "pidsLimit": 256,
        "sharedMemoryMb": 64,
        "dockerLogMaxSizeMb": 10,
        "dockerLogMaxFiles": 3,
        "runnerPort": 8080,
        "cpuLimit": 0.5,
    }


class HostedRunnerResourceContractTests(unittest.TestCase):
    def run_provision_action(
        self,
        action_key: str,
        existing_volumes: dict[str, dict[str, str]] | None = None,
    ):
        module = load_action(action_key)
        calls = []
        volumes = dict(existing_volumes or {})

        def fake_host(*arguments, check=True):
            calls.append(arguments)
            if arguments[:3] == ("docker", "volume", "inspect"):
                name = arguments[3]
                if name not in volumes:
                    return subprocess.CompletedProcess(arguments, 1, "", f"No such volume: {name}")
                return subprocess.CompletedProcess(
                    arguments,
                    0,
                    json.dumps([{"Name": name, "Labels": volumes[name]}]),
                    "",
                )
            if arguments[:3] == ("docker", "volume", "create"):
                name = arguments[-1]
                labels = {}
                for index, argument in enumerate(arguments):
                    if argument == "--label":
                        key, value = arguments[index + 1].split("=", 1)
                        labels[key] = value
                volumes[name] = labels
                return subprocess.CompletedProcess(arguments, 0, f"{name}\n", "")
            stdout = "container-id\n" if arguments[:2] == ("docker", "run") else ""
            return subprocess.CompletedProcess(arguments, 0, stdout, "")

        with (
            patch.object(module, "host", side_effect=fake_host),
            patch.dict(os.environ, {"AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN": "re_test"}),
            patch.object(sys, "stdin", io.StringIO(json.dumps(action_request()))),
            patch.object(sys, "stdout", io.StringIO()),
        ):
            module.main()

        return calls, volumes

    def assert_resource_contract(self, calls):
        run = next(call for call in calls if call[:2] == ("docker", "run"))
        expected_pairs = {
            "--cpus": "0.5",
            "--memory": "384m",
            "--memory-swap": "384m",
            "--pids-limit": "256",
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
        self.assertIn("automaton.managed=true", run)
        self.assertIn("automaton.resource=hosted-runner", run)
        self.assertIn(f"{SYSTEM_VOLUME}:/automaton/system", run)
        self.assertIn(f"{USER_VOLUME}:/automaton/storage", run)
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
        self.assert_resource_contract(calls)

    def test_valid_existing_volumes_are_reused_without_recreation(self):
        existing = {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        }
        calls, _ = self.run_provision_action("reprovision-hosted-runner", existing)

        self.assertFalse(any(call[:3] == ("docker", "volume", "create") for call in calls))
        self.assert_resource_contract(calls)

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
        self.assertEqual(sum(call[:3] == ("docker", "volume", "inspect") for call in calls), 2)
        self.assertEqual(sum(call[:3] == ("docker", "volume", "rm") for call in calls), 2)

    def test_delete_refuses_unlabeled_or_mismatched_volume(self):
        invalid_labels = [
            {},
            volume_labels("system", "88888888-8888-4888-8888-888888888888"),
            {**volume_labels("system"), "automaton.storage-scope": "user"},
        ]
        for labels in invalid_labels:
            with self.subTest(labels=labels):
                with self.assertRaisesRegex(RuntimeError, "ownership labels"):
                    self.run_delete_action(
                        False,
                        {
                            SYSTEM_VOLUME: labels,
                            USER_VOLUME: volume_labels("user"),
                        },
                    )

    def test_delete_requires_boolean_volume_disposition(self):
        with self.assertRaisesRegex(ValueError, "explicitly"):
            self.run_delete_action("preserve", {})


if __name__ == "__main__":
    unittest.main()
