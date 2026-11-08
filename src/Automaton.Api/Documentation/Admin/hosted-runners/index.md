---
title: Runner Administration
order: 20
---

# Runner Administration

Machine Management is the supported Runner profile. Install it from **Server → Add a Runner**, then assign it to the app environment. Each machine runs one management Runner and can host multiple isolated applications.

- [Server installation and setup](./server-installation.md)
- [Environments, deployment targets, and Runner assignments](./deployment-targets.md)
- [Pause public registration](./public-registration.md)

The former Admin Runner screens, Infrastructure Runner installer, and Hosted Runner provisioning are retired. Existing Hosted Runner and Runner Host database records remain as history; this change does not migrate or delete their data or containers.

The former bundled System Application page and its privileged schedules are retired. Create an ordinary App with administrator access for future system tasks. Existing schedule records are retained but do not dispatch.

## Sharing Runners

A Runner belongs to the person who installed it. Other people use it only through a project; there are no server-wide Runners, and a Runner cannot be handed to another user as their personal Runner.

**One person on their own servers.** Install one or more Runners as yourself. With one Runner, deploys use it automatically. With several, choose one per app with `automaton deploy --runner <name>` or `automaton set-runner <name>`.

**An organization.** A server administrator (or anyone who owns a Runner):

1. creates a project and attaches the Runner on the project's **Runners** tab;
2. adds the people who build apps as **Developer** on the **Members** tab (they need an account first; see [Pause public registration](./public-registration.md));
3. each developer creates an app and links it to the project. Deploys of project apps can then use the project's Runner.

People who only use apps need no Runner and no project role beyond what the app requires.

Good to know:

- Every member of a project can see the project's apps.
- One Runner can be attached to several projects and serves all of them.
- People outside the project cannot see the Runner or the project's apps, and cannot change either.
- Only the Runner's owner can rename it, change its settings or upgrade it; the owner can upgrade it while projects use it, since an upgrade does not change who may use it. Project members, including Project Admins, use it for deploys; Project Admins decide whether it is attached to their project.
- Removing someone from the project stops their deploys to its Runner straight away. Apps they created stay theirs, but cannot deploy until they are assigned a Runner they can use.
- A project for one person to experiment in, or one everyone can deploy to, is just another project you can create. Automaton doesn't create these for you.

## Upgrade a Machine Management Runner

A Runner on an old image shows **Update available** on its card, with an **Upgrade** button, a banner on **Runners**, and a count in **Admin → Installation**. The owner clicks **Upgrade → Upgrade now**: the Runner replaces itself on the server's current image, reconnects with its identity and apps, and Studio reports that it reconnected or that it was rolled back to the previous container. The upgrade is refused while a deploy or an Action runs on the Runner, and for an offline Runner; use **Run it manually** (a host command) for those and for a Runner too old to upgrade itself. With a Developer API key, `automaton runners upgrade <runner> --wait` does the same and exits non-zero on failure; `automaton server update --wait --runners` also upgrades the Runners left on an old image.

## Remove a Machine Management Runner

The personal owner uses **Server → Delete Runner → Prepare removal**. First move or delete every assigned app environment, including stopped or undeployed environments, remove it from active projects (deleted projects do not count; a deleted project also stops giving its members access to the Runner, and gets it back if the project is reactivated), and wait for pending Runner operations to finish. The API rejects removal and names the blocking environments; it never silently unassigns them.

With a Developer API key, `automaton runners delete <runner>` performs the same preparation and prints the host uninstall command. After it succeeds, `automaton runners delete <runner> --host-cleanup-confirmed` removes the registration. The key holder must still be the Runner's personal owner; project membership alone does not allow deletion.

Preparation persists a removal intent that blocks new assignments and Runner operations. Studio displays an installation-specific shell command. Run it on the Runner's server with Docker access. It verifies the management container's installation ID and persisted Runner ID, including when the container is stopped. Installation identity is issued during enrollment and cannot be replaced through the Runner update API.

The command refuses to continue if app/Action containers remain. It also inspects managed Compose app data volumes: volumes belonging to this Runner, or older volumes without a valid machine identity, block uninstall until the host administrator has inspected and cleaned up the retained data. Volumes clearly attributed to another Runner are preserved. The command then removes the management container, the empty shared static server, the workspace volume and the installation runtime network. It reports that the host installation/configuration directory is retained; after credential revocation, the host administrator can remove that directory separately. Data retained after an environment was detached must be cleaned up explicitly on that host; the command never blindly deletes remaining app data volumes.

After the command succeeds, select **I ran the command successfully on the Runner server**, then **Confirm removal**. The API requires preparation, owner confirmation, no remaining dependencies, and an offline Runner before removing its registration and deactivating its API key and broker access. Host cleanup is owner-confirmed; Studio cannot independently inspect the host after its Runner is uninstalled.

If cleanup fails, fix the reported problem and rerun the same command. Closing the dialog leaves removal pending; use **Delete runner** again to reopen the command. Do not confirm successful host cleanup after a failed command.

Apply the Runner removal-state migration before releasing the API. Drain older API replicas: they do not enforce removal admission. Release the matching Runner version before testing app deletion; older Runners reject the new cleanup command rather than reporting incomplete cleanup as successful.
