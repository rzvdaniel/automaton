#!/usr/bin/env bash
set -euo pipefail
network=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["dockerNetwork"])')
printf -v network_command '%q ' docker network inspect "$network"
json_escape() {
  printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e ':a' -e 'N' -e '$!ba' -e 's/\n/\\n/g'
}
host_exec() {
  automaton-host-exec sh -c "$1"
}
hostname_value=$(host_exec 'hostname 2>/dev/null' || printf 'unknown')
distribution=$(host_exec "awk -F= '\$1==\"ID\"{gsub(/\"/,\"\",\$2); print \$2}' /etc/os-release 2>/dev/null" || printf 'unknown')
os_version=$(host_exec "awk -F= '\$1==\"VERSION_ID\"{gsub(/\"/,\"\",\$2); print \$2}' /etc/os-release 2>/dev/null" || printf 'unknown')
kernel=$(host_exec 'uname -r 2>/dev/null' || printf 'unknown')
architecture=$(host_exec 'uname -m 2>/dev/null' || printf 'unknown')
cpu_count=$(host_exec 'nproc 2>/dev/null || getconf _NPROCESSORS_ONLN 2>/dev/null' || printf '0')
cpu_model=$(host_exec "awk -F: '/model name|Hardware/{gsub(/^ +/,\"\",\$2); print \$2; exit}' /proc/cpuinfo 2>/dev/null" || printf 'unknown')
memory=$(host_exec "free -m 2>/dev/null | awk '/^Mem:/{print \$2}'" || printf '0')
disk=$(host_exec "df -Pm / 2>/dev/null | awk 'NR==2{print \$2}'" || printf '0')
docker_version=$(host_exec "docker version --format '{{.Server.Version}}' 2>/dev/null" || printf 'unavailable')
docker_status=$(host_exec 'docker info >/dev/null 2>&1' && printf 'running' || printf 'unavailable')
docker_storage_driver=$(host_exec "docker info --format '{{.Driver}}' 2>/dev/null" || printf 'unknown')
docker_cgroup_version=$(host_exec "docker info --format '{{.CgroupVersion}}' 2>/dev/null" || printf 'unknown')
network_status=$(host_exec "$network_command >/dev/null 2>&1" && printf 'present' || printf 'missing')
runner_container=$(host_exec "docker ps --filter 'name=automaton-infrastructure-runner' --format '{{.Status}}' 2>/dev/null" || printf 'unknown')
ports=$(host_exec "ss -lnt 2>/dev/null | awk 'NR>1{print \$4}' | head -50 | paste -sd ',' -" || printf 'unknown')
interfaces=$(host_exec "ip -o link show 2>/dev/null | awk -F': ' '{print \$2}' | paste -sd ',' -" || printf 'unknown')
private_ip=$(host_exec "ip route get 1.1.1.1 2>/dev/null | awk '{for (i=1;i<=NF;i++) if (\$i==\"src\") {print \$(i+1); exit}}'" || printf 'unknown')
timestamp_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
docker_health=$(test "$docker_status" = running && printf 'Healthy' || printf 'Unhealthy')
network_health=$(test "$network_status" = present && printf 'Healthy' || printf 'Unhealthy')
printf '{"totalCpuCores":%s,"totalMemoryMb":%s,"totalDiskMb":%s,"metadata":{"hostname":"%s","os":{"distribution":"%s","version":"%s","kernel":"%s"},"architecture":"%s","docker":{"version":"%s","status":"%s","storageDriver":"%s","cgroupVersion":"%s"},"cpu":{"model":"%s","cores":%s},"memoryMb":%s,"diskMb":%s,"network":{"privateIp":"%s","interfaces":"%s","automatonNetwork":"%s","availableRuntimePorts":"%s"},"runtime":{"infrastructureRunnerContainer":"%s"}},"health":{"validationVersion":"host-validation.v1","timestampUtc":"%s","docker":"%s","network":"%s","disk":"%s","apiConnectivity":"Healthy","warnings":[],"errors":[]}}\n' \
  "${cpu_count:-0}" "${memory:-0}" "${disk:-0}" "$(json_escape "$hostname_value")" "$(json_escape "$distribution")" "$(json_escape "$os_version")" "$(json_escape "$kernel")" "$(json_escape "$architecture")" "$(json_escape "$docker_version")" "$(json_escape "$docker_status")" "$(json_escape "$docker_storage_driver")" "$(json_escape "$docker_cgroup_version")" "$(json_escape "$cpu_model")" "${cpu_count:-0}" "${memory:-0}" "${disk:-0}" "$(json_escape "$private_ip")" "$(json_escape "$interfaces")" "$(json_escape "$network_status")" "$(json_escape "$ports")" "$(json_escape "$runner_container")" "$(json_escape "$timestamp_utc")" "$(json_escape "$docker_health")" "$(json_escape "$network_health")" "$(test "${disk:-0}" -gt 0 2>/dev/null && printf 'Healthy' || printf 'Unhealthy')"

