---
title: Runner Administration
order: 20
---

# Runner Administration

Machine Management is the supported Runner profile. Install it from **Runners → Install Machine Management Runner**, then assign it to the app environment. Each machine runs one management Runner and can host multiple isolated applications.

- [Server installation and setup](./server-installation.md)
- [Environments, deployment targets, and Runner assignments](./deployment-targets.md)
- [Pause public registration](./public-registration.md)

The former Admin Runner screens, Infrastructure Runner installer, and Hosted Runner provisioning are retired. Existing Hosted Runner and Runner Host database records remain as history; this change does not migrate or delete their data or containers.

The former bundled System Application page and its privileged schedules are retired. Create an ordinary App with administrator access for future system tasks. Existing schedule records are retained but do not dispatch.
