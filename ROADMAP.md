# Roadmap

Where Automaton is going, in plain words. Each line links to its GitHub issue. This is a direction, not a promise: there are no dates, and the order can change.

## Now

### AI-first setup
- An AI agent can sign the CLI in and get an API key without a browser, from the docs alone ([#1697](https://github.com/rzvdaniel/automaton/issues/1697))
- Install docs: AI agents hand over a password link and never print the backup key ([#1698](https://github.com/rzvdaniel/automaton/issues/1698))
- Install and Work with AI docs: the real DNS records, and the install link ([#1699](https://github.com/rzvdaniel/automaton/issues/1699))
- Create a server that installs Automaton on first boot from a cloud-init snippet ([#1700](https://github.com/rzvdaniel/automaton/issues/1700))

### App catalog
- Visitors can browse the app catalog on automaton.monster before installing ([#1708](https://github.com/rzvdaniel/automaton/issues/1708))
- Catalog apps carry an icon and screenshots that every client shows ([#1709](https://github.com/rzvdaniel/automaton/issues/1709))
- automaton.monster has a Catalog menu, search and a page per app ([#1710](https://github.com/rzvdaniel/automaton/issues/1710))

## Next

### Tools from the catalog
- Admins can install Adminer from the catalog to manage databases in the browser ([#1701](https://github.com/rzvdaniel/automaton/issues/1701))
- Admins can install PostgreSQL from the catalog and connect their apps to it ([#1702](https://github.com/rzvdaniel/automaton/issues/1702))
- Admins can install a file browser from the catalog ([#1703](https://github.com/rzvdaniel/automaton/issues/1703))
- Admin tools can open another app's data with the owner's approval ([#1704](https://github.com/rzvdaniel/automaton/issues/1704))
- Admins can install wg-easy from the catalog for private VPN access to the server ([#1707](https://github.com/rzvdaniel/automaton/issues/1707))
- The catalog offers the tools admins expect on a managed server ([#1705](https://github.com/rzvdaniel/automaton/issues/1705))

### Open standards and sign-in
- The app and catalog format is an open, versioned spec other platforms can implement ([#1716](https://github.com/rzvdaniel/automaton/issues/1716))
- Apps sign people in with their Automaton account through OpenID Connect ([#1715](https://github.com/rzvdaniel/automaton/issues/1715))

### Hosting
- Automaton runs behind the Cloudflare proxy or a Cloudflare Tunnel ([#1712](https://github.com/rzvdaniel/automaton/issues/1712))

## Later

### Scale and recovery
- An owner can run apps across several servers and regions, and move them between zones ([#1717](https://github.com/rzvdaniel/automaton/issues/1717))
- Backups are restored and checked automatically, and can go encrypted to any S3 storage ([#1721](https://github.com/rzvdaniel/automaton/issues/1721))

### Trust and security
- Automaton passes an external security audit and isolates apps from untrusted catalogs ([#1718](https://github.com/rzvdaniel/automaton/issues/1718))
- Releases and catalog entries are signed and builds are reproducible ([#1719](https://github.com/rzvdaniel/automaton/issues/1719))
- People sign in to Automaton with their company login, two-factor or a passkey ([#1720](https://github.com/rzvdaniel/automaton/issues/1720))

### Reach
- Automaton installs on home and office hardware like a Raspberry Pi or a mini PC ([#1723](https://github.com/rzvdaniel/automaton/issues/1723))
- Studio is accessible and available in several languages ([#1722](https://github.com/rzvdaniel/automaton/issues/1722))

Want something that is not here? [Open an issue](https://github.com/rzvdaniel/automaton/issues/new).
