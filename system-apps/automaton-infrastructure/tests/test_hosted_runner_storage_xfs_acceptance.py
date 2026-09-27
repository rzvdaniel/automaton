"""Opt-in Docker + real-XFS acceptance for the Hosted Runner quota boundary.

Run on a dedicated Linux host with Docker's volume data root on XFS mounted
with project quotas, as root, and with a small Linux utility image cached:

    AUTOMATON_XFS_ACCEPTANCE_IMAGE=busybox:1.36 python3 -m unittest \\
        test_hosted_runner_storage_xfs_acceptance

The quota test creates uniquely named temporary Docker volumes and removes
only those volumes. Shared-store exhaustion additionally requires all of:

    AUTOMATON_SHARED_STORAGE_ACCEPTANCE=1
    AUTOMATON_SHARED_STORAGE_ACCEPTANCE_FS_UUID=<expected mounted XFS UUID>
    AUTOMATON_SHARED_STORAGE_ACCEPTANCE_IMAGE=<uncached image larger than remaining free space>
    AUTOMATON_SHARED_STORAGE_ACCEPTANCE_MAX_FILL_MIB=<operator-approved temporary fill ceiling>

The shared-store test is intentionally skipped unless explicitly enabled and
the proposed fill remains under the supplied ceiling.
"""

import importlib.util
import json
import os
import subprocess
import sys
import shutil
import shlex
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
        self.assert_writable_root_layer_is_bounded()

        self.replace_runner_container_and_verify_data(mountpoints, filesystem_uuid)
        self.assertEqual(self.read_image_file(system, "/system/runner/marker"), "preserved-system")
        self.assertEqual(self.read_image_file(user, "/user/workspace/data/app/marker"), "preserved-user")
        usage, hard_limit = ACTION.read_project_quota(ACTION.resolve_filesystem_mountpoint(mountpoints[0]), self.project_id)
        self.assertGreater(usage, 0)
        self.assertEqual(hard_limit, 64 * 1024 * 1024)

    @unittest.skipUnless(
        os.environ.get("AUTOMATON_SHARED_STORAGE_ACCEPTANCE") == "1",
        "set AUTOMATON_SHARED_STORAGE_ACCEPTANCE=1 to run shared Docker storage exhaustion acceptance",
    )
    def test_shared_storage_exhaustion_fails_image_pull_and_preserves_tenant_and_infrastructure(self):
        _, mount = self.verify_shared_storage_layout()
        expected_uuid = os.environ["AUTOMATON_SHARED_STORAGE_ACCEPTANCE_FS_UUID"]
        self.assertEqual(mount["uuid"].lower(), expected_uuid.lower())
        image = os.environ["AUTOMATON_SHARED_STORAGE_ACCEPTANCE_IMAGE"]
        self.assertNotEqual(
            subprocess.run(["docker", "image", "inspect", image], capture_output=True, check=False).returncode,
            0, "the acceptance image must be uncached before the test",
        )
        runner = self.infrastructure_runner_status()

        volume = self.volumes["user"]
        mountpoint = self.volume_mountpoint(volume)
        ACTION.ensure_xfs_project_quota([mountpoint], self.project_id, 64, self.runner_id)
        tenant_marker = Path(mountpoint) / "acceptance" / "tenant-data.txt"
        tenant_marker.parent.mkdir(parents=True, exist_ok=True)
        tenant_marker.write_text("preserved", encoding="utf-8")
        _, tenant_limit = ACTION.read_project_quota(
            ACTION.resolve_filesystem_mountpoint(mountpoint), self.project_id,
        )

        free_bytes = shutil.disk_usage(mount["target"]).free
        remaining_bytes = 64 * 1024 * 1024
        required_fill = max(0, free_bytes - remaining_bytes)
        max_fill = int(os.environ["AUTOMATON_SHARED_STORAGE_ACCEPTANCE_MAX_FILL_MIB"]) * 1024 * 1024
        if required_fill > max_fill:
            self.skipTest(f"filling {required_fill // (1024 * 1024)} MiB exceeds the explicitly approved test limit")

        filler = Path(mount["target"]) / f".automaton-storage-acceptance-{uuid.uuid4().hex}"
        pull = None
        try:
            self.fill_filesystem(filler, required_fill)
            pull = subprocess.run(["docker", "pull", image], text=True, capture_output=True, check=False)
            self.assertNotEqual(pull.returncode, 0, "the uncached image unexpectedly fit in exhausted shared storage")
            self.assertRegex((pull.stderr + pull.stdout).lower(), r"no space left|not enough space|insufficient space")
            self.assertEqual(tenant_marker.read_text(encoding="utf-8"), "preserved")
            _, current_tenant_limit = ACTION.read_project_quota(
                ACTION.resolve_filesystem_mountpoint(mountpoint), self.project_id,
            )
            self.assertEqual(current_tenant_limit, tenant_limit)
            self.assertEqual(self.infrastructure_runner_status(), runner)
        finally:
            filler.unlink(missing_ok=True)
            if pull is not None and pull.returncode == 0:
                subprocess.run(["docker", "image", "rm", image], capture_output=True, check=False)

    def verify_shared_storage_layout(self):
        docker_root = self.docker("info", "--format", "{{.DockerRootDir}}")
        driver_status = json.loads(self.docker("info", "--format", "{{json .DriverStatus}}"))
        image_store_type = next(
            (row[1] for row in driver_status if len(row) >= 2 and row[0] == "driver-type"), None,
        )
        if image_store_type and image_store_type.startswith("io.containerd.snapshotter."):
            containerd_root = self.containerd_image_store_root()
        elif self.docker("info", "--format", "{{.Driver}}") in {
            "overlay2", "overlayfs", "aufs", "btrfs", "zfs", "devicemapper", "vfs", "fuse-overlayfs",
        }:
            containerd_root = docker_root
        else:
            self.fail("Docker's active image storage backend could not be identified")
        docker_mount = self.find_mount(docker_root)
        containerd_mount = self.find_mount(containerd_root)
        self.assertEqual(docker_mount["fstype"], "xfs")
        self.assertEqual(containerd_mount["fstype"], "xfs")
        self.assertTrue({"prjquota", "pquota"}.intersection(docker_mount["options"].split(",")))
        self.assertEqual(docker_mount["target"], containerd_mount["target"])
        self.assertEqual(docker_mount["uuid"], containerd_mount["uuid"])
        self.assertNotEqual(docker_mount["target"], "/")
        return docker_root, docker_mount

    def containerd_image_store_root(self):
        docker_processes = self.process_arguments("dockerd")
        if len(docker_processes) != 1:
            self.fail("the Docker daemon connected to the active image store is ambiguous")
        docker_socket = self.read_option(docker_processes[0], "--containerd") or "/run/containerd/containerd.sock"
        containerd_processes = self.process_arguments("containerd")
        active_processes = [
            arguments for arguments in containerd_processes
            if (self.read_option(arguments, "--address", "-a") or "/run/containerd/containerd.sock") == docker_socket
        ]
        if len(active_processes) != 1:
            self.fail("the containerd process connected to Docker's image store is ambiguous or missing")
        arguments = active_processes[0]
        root = self.read_option(arguments, "--root", "-r")
        if root:
            return root
        config_path = self.read_option(arguments, "--config", "-c") or "/etc/containerd/config.toml"
        config = subprocess.run(
            ["containerd", "--config", config_path, "config", "dump"],
            text=True, capture_output=True, check=True,
        ).stdout
        root_line = next((line for line in config.splitlines() if line.strip().startswith("root =")), "")
        return root_line.split("=", 1)[1].strip().strip("\"'") if root_line else "/var/lib/containerd"

    @staticmethod
    def process_arguments(name):
        process = subprocess.run(["ps", "-C", name, "-o", "args="], text=True, capture_output=True, check=True)
        return [shlex.split(line) for line in process.stdout.splitlines() if line.strip()]

    @staticmethod
    def read_option(arguments, *names):
        for index, argument in enumerate(arguments):
            for name in names:
                if argument.startswith(f"{name}="):
                    return argument[len(name) + 1:]
                if argument == name and index + 1 < len(arguments):
                    return arguments[index + 1]
        return None

    @staticmethod
    def find_mount(path):
        result = subprocess.run(
            ["findmnt", "--json", "--target", path, "--output", "TARGET,SOURCE,FSTYPE,UUID,OPTIONS"],
            text=True, capture_output=True, check=True,
        )
        filesystems = json.loads(result.stdout)["filesystems"]
        if len(filesystems) != 1:
            raise RuntimeError(f"expected one filesystem for {path}")
        return filesystems[0]

    @staticmethod
    def fill_filesystem(path, size_bytes):
        if size_bytes <= 0:
            return
        descriptor = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        try:
            os.posix_fallocate(descriptor, 0, size_bytes)
            os.fsync(descriptor)
        finally:
            os.close(descriptor)

    def infrastructure_runner_status(self):
        name = os.environ.get("AUTOMATON_INFRASTRUCTURE_RUNNER_CONTAINER", "automaton-infrastructure-runner")
        status = self.docker("ps", "--filter", f"name={name}", "--format", "{{.ID}} {{.Status}}")
        fields = status.split(maxsplit=1)
        self.assertEqual(len(fields), 2, "the Infrastructure Runner must remain reachable and running")
        self.assertIn("Up", fields[1], "the Infrastructure Runner must remain running")
        return fields[0]

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

    def assert_writable_root_layer_is_bounded(self):
        name = f"automaton-m1-xfs-root-layer-{uuid.uuid4().hex}"
        try:
            self.docker(
                "run", "--detach", "--name", name, "--network", "none",
                "--storage-opt", "size=64m", "--entrypoint", "sh", self.image,
                "-c", "sleep 300",
            )
            storage_options = json.loads(self.docker(
                "inspect", "--format", "{{json .HostConfig.StorageOpt}}", name,
            ))
            self.assertEqual(storage_options.get("size"), "64m")

            overflow = subprocess.run(
                ["docker", "exec", name, "sh", "-ec",
                 "dd if=/dev/zero of=/var/tmp/overflow.bin bs=1M count=80 conv=fsync"],
                text=True,
                capture_output=True,
                check=False,
            )
            self.assertNotEqual(overflow.returncode, 0, "writable root layer exceeded its 64 MiB cap")
        finally:
            self.remove_container_if_present(name)
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
