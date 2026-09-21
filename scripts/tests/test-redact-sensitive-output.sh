#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SANITIZER="$ROOT/scripts/redact-sensitive-output.sh"

bash -n "$SANITIZER"

output="$(cat <<'EOF' | "$SANITIZER"
Password=redaction-password
PASSWORD=uppercase-password
password=lowercase-password
RabbitMqPassword=rabbit-password
API_KEY=api-key-value
ApiKey=api-key-value-2
Authorization: Bearer authorization-value
ConnectionString=connection-string-value
MixedCasePrivateKey=private-key-value
safe=value-remains-visible
amqp://runner:uri-password@rabbitmq.example/vhost
amqps://runner:uri-password-tls@rabbitmq.example/vhost
EOF
)"

for secret in \
    redaction-password \
    uppercase-password \
    lowercase-password \
    rabbit-password \
    api-key-value \
    api-key-value-2 \
    authorization-value \
    connection-string-value \
    private-key-value \
    uri-password \
    uri-password-tls; do
    if grep -Fq -- "$secret" <<< "$output"; then
        echo "FAIL: secret value was not redacted: $secret" >&2
        exit 1
    fi
done

grep -Fq -- 'safe=value-remains-visible' <<< "$output"
grep -Fq -- 'Password=[REDACTED]' <<< "$output"
grep -Fq -- 'amqp://runner:[REDACTED]@rabbitmq.example/vhost' <<< "$output"
grep -Fq -- 'amqps://runner:[REDACTED]@rabbitmq.example/vhost' <<< "$output"

echo 'Sensitive-output redaction checks passed.'
