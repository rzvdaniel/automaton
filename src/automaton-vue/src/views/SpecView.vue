<script setup lang="ts">
import BHeader from '@/components/site/b/BHeader.vue'
import BFooter from '@/components/site/b/BFooter.vue'
import BFinal from '@/components/site/b/BFinal.vue'
import { useSiteTheme } from '@/composables/useSiteTheme'
import '@/assets/site-b.css'
useSiteTheme()
</script>

<template>
  <div class="site b">
    <BHeader />
    <main>
<header class="hero"><div class="wrap">
  <div>
    <p class="eyebrow">Spec v1</p>
    <h1>An open catalog format for self-hosted apps.</h1>
    <p class="lede">An app is a folder with an <code>automaton.json</code>. A catalog is a GitHub repository with a <code>catalog.json</code> that lists apps, each pinned to one commit. Both are plain JSON with published schemas, so any platform can read them, and one catalog can serve more than one platform.</p>
    <div class="trust"><span>Text: CC BY 4.0</span><span>Schemas: Apache-2.0</span><span>Version 1</span></div>
  </div>
</div></header>

<section class="band"><div class="wrap">
  <div class="head"><p class="eyebrow">Schemas</p><h2>The contract is the schema</h2></div>
  <ul>
    <li><a href="/schemas/automaton.schema.json"><code>https://automaton.monster/schemas/automaton.schema.json</code></a>: the app manifest.</li>
    <li><a href="/schemas/catalog.schema.json"><code>https://automaton.monster/schemas/catalog.schema.json</code></a>: the catalog index.</li>
  </ul>
  <p class="soft">Both are JSON Schema 2020-12. Automaton's own catalog build and the official catalog are checked against them in CI.</p>
  <p class="soft">Validate a file with any JSON Schema tool, for example:</p>
<pre class="code">pip install jsonschema
curl -sO https://automaton.monster/schemas/catalog.schema.json
jsonschema -i catalog.json catalog.schema.json</pre>
</div></section>

<section class="band alt"><div class="wrap">
  <div class="head"><p class="eyebrow">automaton.json</p><h2>The app</h2></div>
  <p>Required: <code>name</code>. A catalog app also needs <code>summary</code> (one line). Everything an app needs to run sits under <code>application</code>:</p>
  <ul>
    <li><b>Build and run:</b> <code>build</code> (image, command, outputDirectory), <code>runtime</code> (image, command, port, user, update), or <code>compose</code> for a small multi-container app.</li>
    <li><b>Health:</b> <code>health.path</code> answers 2xx when the app is ready; <code>startupTimeoutSeconds</code> bounds the start.</li>
    <li><b>Storage:</b> <code>storage.dataDirectory</code> and <code>mounts</code> survive redeploys; everything else is replaced.</li>
    <li><b>Settings and secrets:</b> named values delivered as environment variables. Secrets are never stored in the manifest.</li>
    <li><b>Permissions:</b> <code>permissions</code> are requests. A manifest or catalog never grants one; an administrator approves each on the installing server.</li>
  </ul>
  <p>Unknown fields are allowed and tools must keep them, so a newer manifest is never damaged by an older tool.</p>
<pre class="code">{
  "name": "Hello",
  "summary": "A page that greets you.",
  "application": {
    "runtime": { "image": "node:22-slim", "command": { "executable": "node", "arguments": ["server.mjs"] }, "port": 8080 },
    "health": { "path": "/health" },
    "storage": { "dataDirectory": "data" },
    "permissions": ["notify:email"]
  }
}</pre>
</div></section>

<section class="band"><div class="wrap">
  <div class="head"><p class="eyebrow">catalog.json</p><h2>The catalog</h2></div>
  <p>Required at the top: <code>schemaVersion</code> (<code>1</code>), <code>repo</code> (<code>owner/name</code>) and <code>apps</code>. Each entry has <code>id</code>, <code>name</code>, <code>summary</code>, <code>category</code>, <code>folder</code>, <code>commit</code> and <code>minServerVersion</code>; <code>permissions</code>, <code>publisher</code>, <code>icon</code> and <code>screenshots</code> are optional.</p>
  <ul>
    <li><b>Pinning:</b> <code>commit</code> is the full SHA of the last commit that changed the folder. An install reads exactly those files, so what was listed is what runs.</li>
    <li><b>Versioning:</b> <code>schemaVersion</code> changes only for a breaking change. New optional fields do not change it; readers ignore fields they do not know and refuse a higher major version.</li>
    <li><b>Safety:</b> <code>folder</code> is a relative path without <code>.</code> or <code>..</code>; <code>minServerVersion</code> lets an older server show an entry without Install.</li>
  </ul>
<pre class="code">{
  "schemaVersion": 1,
  "repo": "example-org/team-tools",
  "apps": [{
    "id": "adminer", "name": "Adminer", "summary": "Manage a database from the browser.",
    "category": "Developer tools", "folder": "tools/adminer",
    "commit": "0123456789abcdef0123456789abcdef01234567",
    "minServerVersion": "0.2.200", "permissions": []
  }]
}</pre>
</div></section>

<section class="band alt"><div class="wrap">
  <div class="head"><p class="eyebrow">Adopt it</p><h2>Start from a Docker Compose file</h2></div>
  <p>The Automaton CLI turns a <code>compose.yaml</code> into an app folder, drops what the platform does not use (restart policies, host port mappings) and refuses what it cannot run safely (build contexts, host mounts):</p>
<pre class="code">automaton import-compose compose.yaml --out my-app --name "My App"</pre>
  <p class="soft">Scope of v1: the app and catalog format. Converters to other platforms' formats and a governance process are later work. Read the reference implementation in <a href="https://github.com/rzvdaniel/automaton" target="_blank" rel="noopener">GitHub</a>; send corrections as issues.</p>
</div></section>
    </main>
    <BFinal />
    <BFooter />
  </div>
</template>
