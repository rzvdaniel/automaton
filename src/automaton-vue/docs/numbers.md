---
title: Automaton by the numbers
description: Measured install, update and restore times, server resources, and project activity.
---

# Automaton by the numbers

These figures describe specific tests, not guarantees or a capacity benchmark. The server measurements below were recorded on 5–9 November 2026. Times include only the named step.

| Measure | Result and scope | Source |
| --- | --- | --- |
| Server install | 118 seconds on Ubuntu 22.04; about 90 seconds on Debian 12, on separate Hetzner CX23 servers. On 9 November 2026 (server 0.2.82 to 0.2.85), fresh-server runs by agents using only the public docs took 94 seconds for the installer on Debian 12 and 2-3 minutes from the first command to a working server on Ubuntu 24.04 | [Fresh-server run notes](https://github.com/rzvdaniel/automaton/issues/561) |
| Server size and price | CX23, 2 vCPU and 4 GB RAM, about €6.64 per month at the time of the run; domain and other services are extra | [Fresh-server run notes](https://github.com/rzvdaniel/automaton/issues/561) |
| Disk and memory after install | About 1.0 GB of memory on the early runs and 1.1 GB on the 9 November run (server 0.2.82). The 9 November run used 4.1 GB of a 38 GB disk after install and 7.4 GB after adding a Runner and one catalog app; this was not a load test | [Fresh-server run notes](https://github.com/rzvdaniel/automaton/issues/561) |
| Runner install | 31 seconds on Ubuntu 22.04; 32 seconds on Debian 12 | [Fresh-server run notes](https://github.com/rzvdaniel/automaton/issues/561), [Debian Runner check](https://github.com/rzvdaniel/automaton/issues/720) |
| Server update | `automaton server update --wait` took 32 seconds from 0.2.21 to 0.2.22 on Ubuntu and 26 seconds from 0.2.28 to 0.2.29 on Debian | [Fresh-server run notes](https://github.com/rzvdaniel/automaton/issues/561), [Debian updater check](https://github.com/rzvdaniel/automaton/issues/719) |
| Clean-server restore | Restore script completed in 13 seconds on a new Ubuntu 24.04 CX23 after the off-site archive had been downloaded and decrypted; transfer and setup time are excluded | [Restore drill](https://github.com/rzvdaniel/automaton/issues/524) |

## Supported systems

The server installer supports x86_64 Linux with systemd. Ubuntu and Debian 12 are supported, with Docker installed automatically if needed; Ubuntu 22.04, Ubuntu 24.04 and Debian 12 have been tested as described in the [install guide](./install-server). Other distributions require Docker Engine and Compose preinstalled and have not been tested. ARM, offline and private-only installs are not supported. The Ubuntu 22.04 and Debian 12 timings above are individual runs, not a complete supported-system matrix.

## Project activity

Counted on 6 November 2026 from the [GitHub releases](https://github.com/rzvdaniel/automaton/releases) and [closed issues](https://github.com/rzvdaniel/automaton/issues?q=is%3Aissue+is%3Aclosed+closed%3A%3E%3D2026-11-03), using 3 November 2026 at 00:00 UTC as the start: 47 releases published and 102 issues closed. These counts change as work continues.

The [CI test run](https://github.com/rzvdaniel/automaton/actions/runs/42299474665) on 6 November 2026 reported 1,904 passing .NET tests and one skipped test across the Architecture, Worker, Runner, Core, API, CLI and AI test projects. This is a test run count, not a count of every check in the repository.
