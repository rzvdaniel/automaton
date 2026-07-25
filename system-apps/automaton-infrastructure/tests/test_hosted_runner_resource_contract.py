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


def load_action(action_key: str):
    path = ROOT / action_key / "main.py"
    spec = importlib.util.spec_from_file_location(f"{action_key}_main", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class HostedRunnerResourceContractTests(unittest.TestCase):
    def run_action(self, action_key: str):
        module = load_action(action_key)
        calls = []

        def fake_host(*arguments, check=True):
            calls.append(arguments)
            stdout = "container-id\n" if arguments[:2] == ("docker", "run") else ""
            return subprocess.CompletedProcess(arguments, 0, stdout, "")

        request = {
            "operationId": "0f24f98f-0f23-4929-b5d7-cdfe27215aac",
            "hostedRunnerId": "2ca73341-c40b-4e1e-a8b8-586d72236475",
            "userId": "88a0b5a9-3b25-4f1b-aa52-0319744ca451",
            "specificationId": "1e3aeb97-ae73-4ba9-9f28-580f5eeaf6d8",
            "containerName": "automaton-hosted-runner-test",
            "runnerName": "Hosted Runner",
            "image": "automaton/runner:test",
            "networkName": "automaton",
            "systemVolumeName": "automaton-hosted-runner-2ca73341c40b4e1ea8b8586d72236475-system",
            "userVolumeName": "automaton-hosted-runner-2ca73341c40b4e1ea8b8586d72236475-user",
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

        with (
            patch.object(module, "host", side_effect=fake_host),
            patch.dict(os.environ, {"AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN": "re_test"}),
            patch.object(sys, "stdin", io.StringIO(json.dumps(request))),
            patch.object(sys, "stdout", io.StringIO()),
        ):
            module.main()

        return calls

    def assert_resource_contract(self, calls):
        volume_calls = [call for call in calls if call[:3] == ("docker", "volume", "create")]
        self.assertEqual(len(volume_calls), 2)
        self.assertTrue(any("automaton.storage-scope=system" in call for call in volume_calls))
        self.assertTrue(any("automaton.storage-scope=user" in call for call in volume_calls))
        for call in volume_calls:
            self.assertIn("automaton.managed=true", call)
            self.assertIn("automaton.resource=hosted-runner-volume", call)

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

    def test_provision_enforces_resources_and_labels_named_volumes(self):
        self.assert_resource_contract(self.run_action("provision-hosted-runner"))

    def test_reprovision_reuses_deterministic_named_volumes(self):
        self.assert_resource_contract(self.run_action("reprovision-hosted-runner"))

    def test_delete_requires_explicit_volume_disposition(self):
        module = load_action("delete-hosted-runner")
        calls = []
        runner_id = "2ca73341-c40b-4e1e-a8b8-586d72236475"
        request = {
            "operationId": "0f24f98f-0f23-4929-b5d7-cdfe27215aac",
            "hostedRunnerId": runner_id,
            "containerName": "automaton-hosted-runner-test",
            "preserveVolumes": True,
            "systemVolumeName": "automaton-hosted-runner-2ca73341c40b4e1ea8b8586d72236475-system",
            "userVolumeName": "automaton-hosted-runner-2ca73341c40b4e1ea8b8586d72236475-user",
        }

        def fake_host(*arguments):
            calls.append(arguments)
            return subprocess.CompletedProcess(arguments, 0, "", "")

        with (
            patch.object(module, "host", side_effect=fake_host),
            patch.object(sys, "stdin", io.StringIO(json.dumps(request))),
            patch.object(sys, "stdout", io.StringIO()),
        ):
            module.main()
        self.assertFalse(any(call[:3] == ("docker", "volume", "rm") for call in calls))

        request["preserveVolumes"] = False
        calls.clear()
        with (
            patch.object(module, "host", side_effect=fake_host),
            patch.object(sys, "stdin", io.StringIO(json.dumps(request))),
            patch.object(sys, "stdout", io.StringIO()),
        ):
            module.main()
        self.assertEqual(sum(call[:3] == ("docker", "volume", "rm") for call in calls), 2)


if __name__ == "__main__":
    unittest.main()
