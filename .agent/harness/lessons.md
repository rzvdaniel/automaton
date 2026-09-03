## 2026-09-02 - Caller lifetime is not deployment lifetime

Problem:
Synchronous package deployment outlived the API request, so a client-visible 524 could be followed by a successful Runner mutation.

Cause:
The request owned the package and deployment pipeline even though Runner build and readiness can take minutes.

Rule:
Accept and persist the package and operation before long-running Runner work. Execute from durable server-owned state, expose a terminal operation status, and fence target completion with the operation identity.

Verification:
Operation persistence/acceptance, CLI polling, and stale-operation target-fencing tests.
