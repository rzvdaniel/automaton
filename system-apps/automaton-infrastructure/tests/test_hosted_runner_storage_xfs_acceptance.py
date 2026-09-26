"""Opt-in Docker + real-XFS acceptance for the Hosted Runner quota boundary.

Run on a dedicated Linux host with Docker's volume data root on XFS mounted
with project quotas, as root, and with a small Linux utility image cached:

    AUTOMATON_XFS_ACCEPTANCE_IMAGE=busybox:1.36 python3 -m unittest \\
        test_hosted_runner_storage_xfs_acceptance

The test creates uniquely named temporary Docker volumes and removes only
those volumes. It is intentionally skipped by the fast unit-test suite unless
the image environment variable is set.
"""

import importlib.util
import os
import subprocess
import sys
import unittest
import uuid
from pathlib import Path


ACTION_PATH = Path(__file__).parents[1] / "actions" / "provision-hosted-runner" / "main.py"
SPEC = importlib.util.spec_from_file_location("provision_hosted_runner_storage", ACTION_PATH)
ACTION = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(ACTION)


class HostedRunnerStorageXfsAcceptanceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.image = os.environ.get("AUTOMATON_XFS_ACCEPTANCE_IMAGE")
        if not cls.image:
            raise unittest.SkipTest("set AUTOMATON_XFS_ACCEPTANCE_IMAGE to run real-XFS acceptance")
        if os.name != "posix" or os.geteuid() != 0:
            raise unittest.SkipTest("real-XFS acceptance requires a root Linux host")
        try:
            cls.docker("image", "inspect", cls.image)
        except RuntimeError as error:
            raise unittest.SkipTest(f"acceptance image must already be cached: {error}") from error

    @classmethod
    def docker(cls, *arguments):
        result = subprocess.run(["docker", *arguments], text=True, capture_output=True, check=False)
        if result.returncode:
            raise RuntimeError(result.stderr.strip() or f"docker {' '.join(arguments)} failed")
        return result.stdout.strip()

    def setUp(self):
        self.runner_id = str(uuid.uuid4())
        compact = self.runner_id.replace("-", "")
        self.project_id = 1_500_000_000 + uuid.uuid4().int % 600_000_000
        self.volumes = {
            "system": f"automaton-hosted-runner-{compact}-system",
            "user": f"automaton-hosted-runner-{compact}-user",
        }
        self.created_volumes = []
        self.original_host = ACTION.host
        ACTION.host = self.run_host_command
        for scope, name in self.volumes.items():
            self.docker(
                "volume", "create",
                "--label", "automaton.managed=true",
                "--label", "automaton.resource=hosted-runner-volume",
                "--label", f"automaton.hosted-runner-id={self.runner_id}",
                "--label", f"automaton.storage-scope={scope}",
                name,
            )
            self.created_volumes.append(name)

    def tearDown(self):
        cleanup_errors = []
        for name in self.created_volumes:
            try:
                self.docker("volume", "rm", name)
            except RuntimeError as error:
                cleanup_errors.append(str(error))
        ACTION.host = self.original_host
        self.assertEqual(cleanup_errors, [])

    def test_combined_quota_bounds_system_app_build_and_tmpfs_and_preserves_data(self):
        system = self.volumes["system"]
        user = self.volumes["user"]
        self.run_image(
            ["--mount", f"type=volume,source={system},target=/system",
             "--mount", f"type=volume,source={user},target=/user"],
            "mkdir -p /system/runner /user/workspace/apps/app/targets/target/artifacts/current "
            "/user/workspace/data/app /user/workspace/cache/build "
            "&& printf preserved-system >/system/runner/marker "
            "&& printf preserved-user >/user/workspace/data/app/marker",
        )

        mountpoints = [self.volume_mountpoint(name) for name in (system, user)]
        filesystem_uuid = ACTION.ensure_xfs_project_quota(
            mountpoints, self.project_id, 64, self.runner_id,
        )
        self.assertTrue(filesystem_uuid)
        for mountpoint in mountpoints:
            self.assertEqual(ACTION.current_project_id(mountpoint), self.project_id)
            self.assert_project_inheritance(mountpoint)
        self.assertEqual(
            ACTION.current_project_id(f"{mountpoints[0]}/runner/marker"), self.project_id,
        )

        self.run_image(
            ["--mount", f"type=volume,source={user},target=/user"],
            "mkdir -p /user/workspace/data/app/new-nested "
            "&& printf inherited >/user/workspace/data/app/new-nested/marker",
        )
        self.assertEqual(
            ACTION.current_project_id(f"{mountpoints[1]}/workspace/data/app/new-nested/marker"),
            self.project_id,
        )
        self.run_image(
            ["--mount", f"type=volume,source={user},target=/user", "-e", "TMPDIR=/user/.tmp",
             "--tmpfs", "/tmp:size=2m,mode=1777"],
            'mkdir -p "$TMPDIR/uploads/transfer" && printf transfer >"$TMPDIR/uploads/transfer/part"',
        )
        self.assertEqual(
            ACTION.current_project_id(f"{mountpoints[1]}/.tmp/uploads/transfer/part"),
            self.project_id,
        )

        self.run_image(
            ["--mount", f"type=volume,source={system},target=/system",
             "--mount", f"type=volume,source={user},target=/user"],
            "dd if=/dev/zero of=/system/runner/runtime.bin bs=1M count=12 conv=fsync status=none "
            "&& dd if=/dev/zero of=/user/workspace/apps/app/targets/target/artifacts/current/app.bin bs=1M count=12 conv=fsync status=none "
            "&& dd if=/dev/zero of=/user/workspace/data/app/data.bin bs=1M count=12 conv=fsync status=none "
            "&& dd if=/dev/zero of=/user/workspace/cache/build/cache.bin bs=1M count=12 conv=fsync status=none",
        )

        overflow_name = f"automaton-m1-xfs-failure-{uuid.uuid4().hex}"
        overflow = self.run_image(
            ["--mount", f"type=volume,source={user},target=/user"],
            "dd if=/dev/zero of=/user/workspace/data/app/overflow.bin bs=1M count=20 conv=fsync status=none",
            succeeds=False,
            name=overflow_name,
        )
        self.assertNotEqual(overflow.returncode, 0)
        self.assertContainerRemoved(overflow_name)

        self.assert_tmpfs_and_read_only_rootfs()

        self.replace_runner_container_and_verify_data(mountpoints, filesystem_uuid)
        self.assertEqual(self.read_image_file(system, "/system/runner/marker"), "preserved-system")
        self.assertEqual(self.read_image_file(user, "/user/workspace/data/app/marker"), "preserved-user")
        usage, hard_limit = ACTION.read_project_quota(ACTION.resolve_filesystem_mountpoint(mountpoints[0]), self.project_id)
        self.assertGreater(usage, 0)
        self.assertEqual(hard_limit, 64 * 1024 * 1024)

    def replace_runner_container_and_verify_data(self, mountpoints, filesystem_uuid):
        name = f"automaton-m1-xfs-reprovision-{uuid.uuid4().hex}"
        system = self.volumes["system"]
        user = self.volumes["user"]
        mount_arguments = [
            "--mount", f"type=volume,source={system},target=/automaton/system",
            "--mount", f"type=volume,source={user},target=/automaton/storage",
        ]
        try:
            self.docker(
                "run", "--detach", "--name", name, "--network", "none", *mount_arguments,
                "--entrypoint", "sh", self.image, "-c", "sleep 300",
            )
            self.docker("rm", "--force", name)
            ACTION.ensure_xfs_project_quota(
                mountpoints, self.project_id, 64, self.runner_id, filesystem_uuid,
            )
            self.docker(
                "run", "--detach", "--name", name, "--network", "none", *mount_arguments,
                "--entrypoint", "sh", self.image, "-c", "sleep 300",
            )
            self.assertEqual(self.read_image_file(system, "/system/runner/marker"), "preserved-system")
            self.assertEqual(self.read_image_file(user, "/user/workspace/data/app/marker"), "preserved-user")
        finally:
            self.remove_container_if_present(name)

    def assert_tmpfs_and_read_only_rootfs(self):
        name = f"automaton-m1-xfs-tmp-{uuid.uuid4().hex}"
        overlay = self.run_image(
            ["--read-only", "--tmpfs", "/tmp:size=2m,mode=1777"],
            "printf blocked >/var/tmp/overlay-write",
            succeeds=False,
            name=name,
        )
        self.assertNotEqual(overlay.returncode, 0)
        self.assertContainerRemoved(name)

        temporary = self.run_image(
            ["--read-only", "--tmpfs", "/tmp:size=2m,mode=1777"],
            "dd if=/dev/zero of=/tmp/overflow.bin bs=1M count=3 conv=fsync status=none",
            succeeds=False,
            name=name,
        )
        self.assertNotEqual(temporary.returncode, 0)
        self.assertContainerRemoved(name)

    def run_host_command(self, *arguments, check=True):
        result = subprocess.run(list(arguments), text=True, capture_output=True, check=False)
        if check and result.returncode:
            raise RuntimeError(result.stderr.strip() or f"host command {arguments[0]} failed")
        return result

    def run_image(self, options, command, succeeds=True, name=None):
        container_name = name or f"automaton-m1-xfs-{uuid.uuid4().hex}"
        arguments = ["run", "--rm", "--name", container_name, "--network", "none", *options,
                     "--entrypoint", "sh", self.image, "-ec", command]
        result = subprocess.run(["docker", *arguments], text=True, capture_output=True, check=False)
        if succeeds and result.returncode:
            self.fail(result.stderr.strip() or "acceptance container failed")
        if succeeds is False and result.returncode == 0:
            self.fail("container command unexpectedly succeeded")
        return result

    def assertContainerRemoved(self, name):
        inspect = subprocess.run(
            ["docker", "container", "inspect", name], text=True, capture_output=True, check=False,
        )
        self.assertNotEqual(inspect.returncode, 0, "failed disposable containers must be removed")

    def remove_container_if_present(self, name):
        inspect = subprocess.run(
            ["docker", "container", "inspect", name], text=True, capture_output=True, check=False,
        )
        if inspect.returncode == 0:
            self.docker("rm", "--force", name)

    def volume_mountpoint(self, volume):
        metadata = ACTION.inspect_volume(volume)
        self.assertIsNotNone(metadata)
        return metadata["mountpoint"]

    def assert_project_inheritance(self, path):
        result = ACTION.host("xfs_io", "-c", "lsattr", path)
        if result.returncode:
            self.fail(result.stderr.strip() or f"Could not inspect XFS flags for {path}")
        flag_line = next((line for line in result.stdout.splitlines() if line.strip()), "")
        flags = flag_line.split(maxsplit=1)[0] if flag_line else ""
        self.assertIn("P", flags, f"XFS project inheritance is not enabled on {path}")

    def read_image_file(self, volume, path):
        return self.docker(
            "run", "--rm", "--network", "none", "--mount", f"type=volume,source={volume},target=/volume,readonly",
            "--entrypoint", "cat", self.image, path.replace("/user", "/volume").replace("/system", "/volume"),
        )


if __name__ == "__main__":
    unittest.main()
