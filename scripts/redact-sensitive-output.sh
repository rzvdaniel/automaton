#!/usr/bin/env bash
set -euo pipefail

# Redact inspection output before it is displayed or recorded. This accepts
# ordinary KEY=value / Key: value lines and also protects AMQP URI passwords.

is_sensitive_key() {
    local key="$1"
    local normalized
    normalized="$(printf '%s' "$key" | tr '[:upper:]' '[:lower:]')"

    case "$normalized" in
        *password*|*passwd*|*secret*|*token*|*api_key*|*apikey*|*authorization*|*connectionstring*|*connection_string*|*credential*|*privatekey*|*private_key*)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

redact_line() {
    local line="$1"

    if [[ "$line" =~ ^([[:alnum:]_.-]+)(=|:)([[:space:]]*)(.*)$ ]]; then
        local key="${BASH_REMATCH[1]}"
        if is_sensitive_key "$key"; then
            printf '%s%s%s[REDACTED]\n' \
                "$key" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}"
            return
        fi
    fi

    line="$(printf '%s' "$line" | sed -E \
        's#(amqps?://[^:/@[:space:]]+):[^/@[:space:]]+@#\1:[REDACTED]@#g')"
    printf '%s\n' "$line"
}

while IFS= read -r line || [[ -n "$line" ]]; do
    redact_line "$line"
done
