import importlib.util
import io
import json
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch


ACTION_PATH = Path(__file__).parents[1] / "actions" / "delete-hosted-runner" / "main.py"
RUNNER_ID = "2ca73341-c40b-4e1e-a8b8-586d72236475"
RUNNER_ID_COMPACT = RUNNER_ID.replace("-", "")
RUNNER_HOST_ID = "41d961ec-a331-47fb-aa86-492bbdde5a7d"
SYSTEM_VOLUME = f"automaton-hosted-runner-{RUNNER_ID_COMPACT}-system"
USER_VOLUME = f"automaton-hosted-runner-{RUNNER_ID_COMPACT}-user"
HOSTED_CONTAINER_ID = "a" * 64
RUNTIME_CONTAINER_ID = "b" * 64
SECOND_RUNTIME_CONTAINER_ID = "c" * 64


def load_action():
    spec = importlib.util.spec_from_file_location("delete_hosted_runner_main", ACTION_PATH)
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


def hosted_runner_container(container_id: str = HOSTED_CONTAINER_ID) -> dict[str, object]:
    return {
        "Id": container_id,
        "Name": "/automaton-hosted-runner-test",
        "Config": {"Labels": {
            "automaton.managed": "true",
            "automaton.resource": "hosted-runner",
            "automaton.hosted-runner-id": RUNNER_ID,
        }},
        "Mounts": [
            {"Type": "volume", "Name": SYSTEM_VOLUME},
            {"Type": "volume", "Name": USER_VOLUME},
        ],
    }


def runtime_container(
    container_id: str = RUNTIME_CONTAINER_ID,
    runner_id: str = RUNNER_ID,
    *,
    volume: str = USER_VOLUME,
    app_id: str = "ee033c26-eaaf-430c-9459-6b99fe992509",
    target_id: str = "192bac08-8217-4ef6-b2a7-bc4a703350ca",
) -> dict[str, object]:
    return {
        "Id": container_id,
        "Name": f"/automaton-app-{app_id}-{target_id}-candidate",
        "Config": {"Labels": {
            "com.automaton.managed": "true",
            "com.automaton.runtime-kind": "generic",
            "com.automaton.app-id": app_id,
            "com.automaton.deployment-target-id": target_id,
            "com.automaton.hosted-runner-id": runner_id,
            "com.automaton.runner-host-id": RUNNER_HOST_ID,
        }},
        "Mounts": [{"Type": "volume", "Name": volume}],
    }


class DeleteHostedRunnerActionTests(unittest.TestCase):
    def run_delete(
        self,
        *,
        preserve_volumes: bool = False,
        containers: list[dict[str, object]] | None = None,
        volumes: dict[str, dict[str, str]] | None = None,
        calls_out: list[tuple[str, ...]] | None = None,
    ) -> tuple[list[tuple[str, ...]], dict[str, dict[str, object]], dict[str, dict[str, str]]]:
        module = load_action()
        calls = calls_out if calls_out is not None else []
        resources = {container["Id"]: container for container in (containers or [])}
        named_resources = {str(container["Name"]).lstrip("/"): container["Id"] for container in resources.values()}
        owned_volumes = dict(volumes or {
            SYSTEM_VOLUME: volume_labels("system"),
            USER_VOLUME: volume_labels("user"),
        })
        request = {
            "operationId": "0f24f98f-0f23-4929-b5d7-cdfe27215aac",
            "hostedRunnerId": RUNNER_ID,
            "containerName": "automaton-hosted-runner-test",
            "preserveVolumes": preserve_volumes,
            "systemVolumeName": SYSTEM_VOLUME,
            "userVolumeName": USER_VOLUME,
        }

        def fake_host(*arguments: str) -> subprocess.CompletedProcess[str]:
            calls.append(arguments)
            if arguments[:3] == ("docker", "volume", "inspect"):
                name = arguments[3]
                labels = owned_volumes.get(name)
                if labels is None:
                    return subprocess.CompletedProcess(arguments, 1, "", f"Error response from daemon: get {name}: no such volume")
                return subprocess.CompletedProcess(arguments, 0, json.dumps([{"Name": name, "Labels": labels}]), "")
            if arguments[:4] == ("docker", "container", "ls", "--all"):
                volume_name = arguments[-1].removeprefix("volume=")
                ids = [
                    str(container_id)
                    for container_id, container in resources.items()
                    if any(
                        isinstance(mount, dict) and mount.get("Name") == volume_name
                        for mount in container["Mounts"]
                    )
                ]
                return subprocess.CompletedProcess(arguments, 0, "\n".join(ids), "")
            if arguments[:3] == ("docker", "container", "inspect"):
                reference = arguments[-1]
                container_id = named_resources.get(reference, reference)
                container = resources.get(container_id)
                if container is None:
                    return subprocess.CompletedProcess(arguments, 1, "", f"Error: No such object: {reference}")
                return subprocess.CompletedProcess(arguments, 0, json.dumps(container), "")
            if arguments[:3] == ("docker", "rm", "-f"):
                reference = arguments[3]
                container_id = named_resources.get(reference, reference)
                container = resources.pop(container_id, None)
                if container is not None:
                    named_resources.pop(str(container["Name"]).lstrip("/"), None)
                return subprocess.CompletedProcess(arguments, 0, "", "")
            if arguments[:3] == ("docker", "volume", "rm"):
                volume_name = arguments[3]
                consumers = [
                    container_id
                    for container_id, container in resources.items()
                    if any(mount.get("Name") == volume_name for mount in container["Mounts"])
                ]
                if consumers:
                    return subprocess.CompletedProcess(
                        arguments,
                        1,
                        "",
                        f"Error response from daemon: remove {volume_name}: volume is in use - {consumers}",
                    )
                owned_volumes.pop(volume_name, None)
                return subprocess.CompletedProcess(arguments, 0, volume_name, "")
            self.fail(f"Unexpected host command: {arguments!r}")

        with (
            patch.object(module, "host", side_effect=fake_host),
            patch.object(sys, "stdin", io.StringIO(json.dumps(request))),
            patch.object(sys, "stdout", io.StringIO()),
        ):
            module.main()
        return calls, resources, owned_volumes

    def test_permanent_delete_removes_all_owned_runtime_containers_before_hosted_runner_and_volumes(self):
        calls, resources, volumes = self.run_delete(
            containers=[hosted_runner_container(), runtime_container()],
        )

        mutations = [
            (index, call)
            for index, call in enumerate(calls)
            if call[:3] in (("docker", "rm", "-f"), ("docker", "volume", "rm"))
        ]
        inspected_container_ids = {
            call[-1]
            for call in calls
            if call[:3] == ("docker", "container", "inspect")
        }
        self.assertTrue({HOSTED_CONTAINER_ID, RUNTIME_CONTAINER_ID}.issubset(inspected_container_ids))
        self.assertEqual(len(mutations), 4)
        self.assertEqual(mutations[0][1], ("docker", "rm", "-f", RUNTIME_CONTAINER_ID))
        self.assertEqual(mutations[1][1], ("docker", "rm", "-f", HOSTED_CONTAINER_ID))
        self.assertEqual([call[1] for _, call in mutations[2:]], ["volume", "volume"])
        self.assertEqual(resources, {})
        self.assertEqual(volumes, {})

    def test_preserve_data_delete_removes_owned_runtime_and_runner_but_keeps_volumes(self):
        calls, resources, volumes = self.run_delete(
            preserve_volumes=True,
            containers=[hosted_runner_container(), runtime_container()],
        )

        removals = [call for call in calls if call[:3] == ("docker", "rm", "-f")]
        self.assertEqual(removals, [
            ("docker", "rm", "-f", RUNTIME_CONTAINER_ID),
            ("docker", "rm", "-f", HOSTED_CONTAINER_ID),
        ])
        self.assertFalse(any(call[:3] == ("docker", "volume", "rm") for call in calls))
        self.assertEqual(resources, {})
        self.assertEqual(set(volumes), {SYSTEM_VOLUME, USER_VOLUME})

    def test_unowned_volume_consumer_fails_before_any_mutation(self):
        unrelated = runtime_container()
        unrelated["Config"]["Labels"].pop("com.automaton.hosted-runner-id")
        calls: list[tuple[str, ...]] = []
        with self.assertRaisesRegex(RuntimeError, "ownership"):
            self.run_delete(containers=[hosted_runner_container(), unrelated], calls_out=calls)
        self.assertFalse(any(call[:3] in (("docker", "rm", "-f"), ("docker", "volume", "rm")) for call in calls))

    def test_malformed_volume_mount_metadata_fails_before_any_mutation(self):
        malformed = runtime_container()
        malformed["Mounts"].append({"Type": "volume"})
        calls: list[tuple[str, ...]] = []
        with self.assertRaisesRegex(RuntimeError, "invalid metadata"):
            self.run_delete(containers=[hosted_runner_container(), malformed], calls_out=calls)
        self.assertFalse(any(call[:3] in (("docker", "rm", "-f"), ("docker", "volume", "rm")) for call in calls))

    def test_mismatched_hosted_runner_label_fails_before_any_mutation(self):
        wrong_owner = runtime_container(runner_id="88888888-8888-4888-8888-888888888888")
        calls: list[tuple[str, ...]] = []
        with self.assertRaisesRegex(RuntimeError, "ownership"):
            self.run_delete(containers=[hosted_runner_container(), wrong_owner], calls_out=calls)
        self.assertFalse(any(call[:3] in (("docker", "rm", "-f"), ("docker", "volume", "rm")) for call in calls))

    def test_already_missing_hosted_runner_still_removes_owned_runtime_and_volumes(self):
        calls, resources, volumes = self.run_delete(containers=[runtime_container()])

        self.assertIn(("docker", "rm", "-f", RUNTIME_CONTAINER_ID), calls)
        self.assertFalse(any(call[:3] == ("docker", "rm", "-f") and call[-1] == "automaton-hosted-runner-test" for call in calls))
        self.assertEqual(resources, {})
        self.assertEqual(volumes, {})

    def test_already_missing_runtime_is_idempotent(self):
        calls, resources, volumes = self.run_delete(containers=[hosted_runner_container()])

        self.assertFalse(any(call[:3] == ("docker", "rm", "-f") and call[-1] == RUNTIME_CONTAINER_ID for call in calls))
        self.assertEqual(resources, {})
        self.assertEqual(volumes, {})

    def test_partial_previous_failure_removes_remaining_runtime_when_runner_is_absent(self):
        calls, resources, volumes = self.run_delete(
            containers=[runtime_container()],
            volumes={USER_VOLUME: volume_labels("user")},
        )

        self.assertEqual(calls[-1], ("docker", "volume", "rm", USER_VOLUME))
        self.assertEqual(resources, {})
        self.assertEqual(volumes, {})

    def test_all_runtime_containers_are_validated_before_any_are_removed(self):
        build = runtime_container(SECOND_RUNTIME_CONTAINER_ID)
        build["Config"]["Labels"] = {
            "automaton.managed": "true",
            "automaton.runtime-kind": "build",
            "automaton.app-id": "ee033c26eaaf430c94596b99fe992509",
            "automaton.deployment-target-id": "192bac0882174ef6b2a7bc4a703350ca",
            "automaton.hosted-runner-id": RUNNER_ID_COMPACT,
            "automaton.runner-host-id": RUNNER_HOST_ID.replace("-", ""),
        }
        calls, resources, _ = self.run_delete(
            containers=[hosted_runner_container(), runtime_container(), build],
            preserve_volumes=True,
        )

        first_mutation = next(
            index for index, call in enumerate(calls)
            if call[:3] == ("docker", "rm", "-f")
        )
        inspected_before_mutation = {
            call[-1]
            for call in calls[:first_mutation]
            if call[:3] == ("docker", "container", "inspect")
        }
        self.assertTrue({HOSTED_CONTAINER_ID, RUNTIME_CONTAINER_ID, SECOND_RUNTIME_CONTAINER_ID}.issubset(inspected_before_mutation))
        self.assertEqual(resources, {})

    def test_concurrent_volume_consumer_failure_reports_the_blocking_container_id(self):
        late_consumer = runtime_container("d" * 64)
        module = load_action()
        calls: list[tuple[str, ...]] = []
        resources = {HOSTED_CONTAINER_ID: hosted_runner_container(), RUNTIME_CONTAINER_ID: runtime_container()}
        volumes = {SYSTEM_VOLUME: volume_labels("system"), USER_VOLUME: volume_labels("user")}

        def fake_host(*arguments: str) -> subprocess.CompletedProcess[str]:
            calls.append(arguments)
            if arguments[:3] == ("docker", "volume", "inspect"):
                name = arguments[3]
                return subprocess.CompletedProcess(arguments, 0, json.dumps([{"Name": name, "Labels": volumes[name]}]), "")
            if arguments[:4] == ("docker", "container", "ls", "--all"):
                volume_name = arguments[-1].removeprefix("volume=")
                ids = [container_id for container_id, container in resources.items()
                       if any(mount.get("Name") == volume_name for mount in container["Mounts"])]
                return subprocess.CompletedProcess(arguments, 0, "\n".join(ids), "")
            if arguments[:3] == ("docker", "container", "inspect"):
                ref = arguments[-1]
                container = next((value for value in resources.values() if value["Name"].lstrip("/") == ref), None)
                container = container or resources.get(ref)
                if container is None:
                    return subprocess.CompletedProcess(arguments, 1, "", f"Error: No such object: {ref}")
                return subprocess.CompletedProcess(arguments, 0, json.dumps(container), "")
            if arguments[:3] == ("docker", "rm", "-f"):
                container_id = arguments[3]
                if container_id == "automaton-hosted-runner-test":
                    container_id = HOSTED_CONTAINER_ID
                resources.pop(container_id, None)
                if container_id == RUNTIME_CONTAINER_ID:
                    resources[late_consumer["Id"]] = late_consumer
                return subprocess.CompletedProcess(arguments, 0, "", "")
            if arguments[:3] == ("docker", "volume", "rm"):
                name = arguments[3]
                if name == SYSTEM_VOLUME:
                    volumes.pop(name, None)
                    return subprocess.CompletedProcess(arguments, 0, name, "")
                blocking_id = late_consumer["Id"]
                return subprocess.CompletedProcess(arguments, 1, "", f"volume is in use by {blocking_id}")
            self.fail(f"Unexpected host command: {arguments!r}")

        request = {
            "operationId": "0f24f98f-0f23-4929-b5d7-cdfe27215aac",
            "hostedRunnerId": RUNNER_ID,
            "containerName": "automaton-hosted-runner-test",
            "preserveVolumes": False,
            "systemVolumeName": SYSTEM_VOLUME,
            "userVolumeName": USER_VOLUME,
        }
        with (
            patch.object(module, "host", side_effect=fake_host),
            patch.object(sys, "stdin", io.StringIO(json.dumps(request))),
            patch.object(sys, "stdout", io.StringIO()),
            self.assertRaisesRegex(RuntimeError, f"{USER_VOLUME}.*{late_consumer['Id']}"),
        ):
            module.main()


if __name__ == "__main__":
    unittest.main()
