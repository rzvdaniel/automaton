# Dependency audit

- Pull requests: `tests.yml` runs `npm audit --omit=dev --audit-level=high` for Studio and the store, and fails only on high or critical advisories in shipped packages. The website step fails on critical only until the picomatch advisory is fixed (#1287).
- Nightly: `dependency-audit.yml` audits all three Vue projects including dev tools. It opens or updates one issue labelled `dependency-advisory` and closes it when the audit is clean. Run it by hand from the Actions tab.
- Dependabot opens weekly npm updates for the three projects, with dev dependencies grouped.
