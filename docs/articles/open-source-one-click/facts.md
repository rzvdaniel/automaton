# Facts for the "running it" part (production, read 2026-11-24)

- **Installed:** the Blog app (Ghost) on 2026-11-15 22:03 UTC, from the catalog. 9 days running.
- **Deploys of the blog itself:** 2 (install, plus one app update on 2026-11-17), 0 failed. Nobody touched Ghost since.
- **Automaton releases since install:** 30 (0.2.158 to 0.2.187). Production took many of them, including today's 0.2.187 from Studio's Update button: 1 min 1 s, blog back on its own.
- **Backups:** nightly at 03:38 UTC, plus one before every update (production keeps the last few).
- **Today's disk move:** Docker's storage moved to a new 40 GB volume. Blog data intact. The blog was down for ~15 minutes: after the restart the Runner kept pointing at the old API instance until it was restarted by hand (now issue #1620). An honest line, if you want it.
- **HTTPS:** certificate issued and renewed automatically by the server; no renewal due yet (90-day certificates, installed 9 days ago).
- **Work by hand on the blog:** none, apart from the Runner restart above.
