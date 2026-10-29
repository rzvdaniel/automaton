<script setup lang="ts">
import { ref } from 'vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import AppFooter from '@/components/AppFooter.vue'
import { publicAppConfig } from '@/config/publicAppConfig'

const copied = ref(false)
const serverInstall =
  'curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh\nsudo bash install-automaton.sh'
const aiPrompt = `Build [APP IDEA] and publish it to my self-hosted Automaton server.
My API URL is https://api.example.com (replace with my server URL).
Read https://automaton.monster/llms.txt and the generated app instructions.
Verify the deployed app's main behavior. Let me approve login in my Studio; never ask for API keys in chat.`

async function copyPrompt() {
  try {
    await navigator.clipboard.writeText(aiPrompt)
    copied.value = true
  } catch {
    copied.value = false
  }
}
</script>

<template>
  <div class="home-v2">
    <PublicSiteHeader />
    <main>
      <header class="hero">
        <div class="shell hero-grid">
          <div>
            <span class="eyebrow">Open source. Self-hosted.</span>
            <h1><span>Build with AI.</span><span>Run it on your server.</span></h1>
            <p class="hero-copy">
              An open-source home for your web apps, APIs and Actions. Install Automaton on your own
              server, build with your AI coding tools, and deploy through one CLI.
            </p>
            <div class="hero-actions">
              <a class="button primary" :href="publicAppConfig.installUrl"
                >Install on your server</a
              >
              <a class="button" :href="publicAppConfig.githubUrl" target="_blank" rel="noopener"
                >View on GitHub</a
              >
            </div>
            <p class="ai-workflow-note">
              Apache 2.0 · Early access · No required Automaton cloud account
            </p>
          </div>
          <aside class="hero-showcase runners-panel" aria-label="Your Automaton installation">
            <div class="runners-panel-header">
              <h3>studio.your-domain.com</h3>
              <span class="status">Your server</span>
            </div>
            <div class="runners-list">
              <div class="runner-item">
                <strong>Studio + API</strong>
                <p>Your users, apps, access rules and deployment controls on your own domain.</p>
              </div>
              <div class="runner-item">
                <strong>Machine Management Runner</strong>
                <p>
                  Hosts static sites, APIs and Actions. Run it on this server or another machine.
                </p>
              </div>
              <div class="runner-item">
                <strong>Your application URLs</strong>
                <p>
                  HTTPS routes to your deployed apps. Persistent data stays on the selected machine.
                </p>
              </div>
            </div>
            <div class="hero-actions">
              <a class="button" :href="publicAppConfig.installUrl">Set up your Studio</a>
            </div>
          </aside>
        </div>
      </header>

      <section id="architecture" class="architecture-section">
        <div class="shell">
          <div class="architecture-intro">
            <div>
              <span class="eyebrow">How it works</span>
              <h2>One installation.<br />Your infrastructure.</h2>
            </div>
            <div class="path-cards">
              <div class="path-card frontend">
                <span>Control plane</span><strong>Your Studio + API</strong>
              </div>
              <div class="path-card backend">
                <span>Workloads</span><strong>Your Runner machines</strong>
              </div>
            </div>
          </div>
          <div class="studio-caption">
            <div class="caption-card">
              <strong>1. Install the server</strong
              ><span
                >Set up your own Studio, API and supporting services. Create your administrator in
                Studio.</span
              >
            </div>
            <div class="caption-card">
              <strong>2. Connect a Runner</strong
              ><span
                >Use the same server or a separate machine. Add machines and choose each app's
                destination.</span
              >
            </div>
            <div class="caption-card">
              <strong>3. Build and deploy</strong
              ><span
                >Build locally with your AI tools, deploy through the CLI, and verify the running
                app.</span
              >
            </div>
          </div>
        </div>
      </section>

      <section id="install" class="ai-workflow-section">
        <div class="shell ai-workflow-grid">
          <div class="ai-workflow-copy">
            <span class="eyebrow">Install and update</span>
            <h2>A server you can install.<br />An updater you can run.</h2>
            <p class="ai-workflow-lead">
              The installer downloads a verified release, prepares credentials, and starts Studio,
              API and supporting services. Docker is installed if missing. Install a Machine
              Management Runner from Studio to host your apps.
            </p>
            <p class="ai-workflow-note">
              Ubuntu 24.04+ · x86_64 · domain and DNS · 10 GiB free disk · ports 80, 443 and 5671
            </p>
            <div class="ai-workflow-actions">
              <a class="button primary" :href="publicAppConfig.installUrl">Installation guide</a
              ><a class="button" href="/cli">CLI setup</a>
            </div>
          </div>
          <div class="terminal-shell">
            <div class="terminal-top">
              <span class="terminal-dot"></span><span class="terminal-dot"></span
              ><span class="terminal-dot"></span
              ><span class="terminal-title">automaton / server</span>
            </div>
            <div class="terminal-body">
              <span class="terminal-comment"># download, review, then install</span>
              <pre class="server-command">{{ serverInstall }}</pre>
              <span class="terminal-comment"># update your installed server</span>
              <pre class="server-command">sudo bash /opt/automaton-server/update.sh</pre>
              <p class="ai-workflow-note">
                Updates make a local recovery backup. API, Studio and Worker may briefly restart;
                plan your update window.
              </p>
            </div>
          </div>
        </div>
      </section>

      <section id="ai-workflow" class="ai-workflow-section">
        <div class="shell ai-workflow-grid">
          <div class="ai-workflow-copy">
            <span class="eyebrow">Your AI workflow</span>
            <h2>Keep the tools<br />you already use.</h2>
            <p class="ai-workflow-lead">
              Use Claude Code, Codex, Cursor, Copilot or another coding agent. The CLI generates
              app-specific instructions for builds, deployment and verification.
            </p>
            <p class="ai-workflow-note">
              Connect with
              <code>automaton login --server https://api.your-domain.com</code>
              and approve login in your own Studio. Keep credentials out of AI chat.
            </p>
            <div class="ai-workflow-actions">
              <button class="button primary" type="button" @click="copyPrompt">
                {{ copied ? 'Prompt copied' : 'Copy starter prompt' }}</button
              ><a class="ai-workflow-link" href="/docs/getting-started">Build your first app</a>
            </div>
          </div>
          <div class="ai-steps-card">
            <span class="eyebrow">Starter prompt</span>
            <pre class="server-command">{{ aiPrompt }}</pre>
          </div>
        </div>
      </section>

      <section id="runners">
        <div class="shell runners-band">
          <div class="runners-copy">
            <h2>Your machines.<br />Your apps and data.</h2>
            <p>
              Start on one server. Add Runner machines when you need more capacity and explicitly
              assign workloads. App data survives normal redeploys; moving data between machines is
              a separate operation.
            </p>
            <div class="runner-chip-grid">
              <span class="runner-chip">Static sites</span><span class="runner-chip">HTTP APIs</span
              ><span class="runner-chip">Actions</span
              ><span class="runner-chip">Persistent storage</span>
            </div>
          </div>
          <div class="runners-panel">
            <div class="runners-panel-header">
              <h3>What runs where?</h3>
              <span class="status">Your infrastructure</span>
            </div>
            <div class="runners-list">
              <div class="runner-item">
                <strong>Studio + API</strong>
                <p>Your users, app configuration, deployment state and access management.</p>
              </div>
              <div class="runner-item">
                <strong>Machine Management Runners</strong>
                <p>
                  Shared static serving, bounded app containers, builds and Actions on each machine.
                </p>
              </div>
              <div class="runner-item">
                <strong>Your operating responsibilities</strong>
                <p>
                  Server capacity, updates, backups and recovery. Automaton does not automatically
                  provision machines or move persistent data.
                </p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section class="cta">
        <div class="shell cta-box">
          <div class="cta-copy">
            <h2>Make it yours.</h2>
            <p>
              Free software under Apache 2.0. You cover your servers, domain and any AI services you
              choose. No required Automaton subscription.
            </p>
          </div>
          <div class="cta-actions">
            <a class="button primary" :href="publicAppConfig.installUrl">Install Automaton</a
            ><a class="button" href="/pricing">Open source &amp; hosting</a>
          </div>
        </div>
      </section>
    </main>
    <AppFooter />
  </div>
</template>

<style scoped>
.server-command {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  font: inherit;
  line-height: 1.7;
}

.home-v2 {
  min-height: 100vh;
  background:
    radial-gradient(circle at 24% 0%, rgba(80, 215, 255, 0.12), transparent 30rem),
    radial-gradient(circle at 86% 10%, rgba(69, 230, 138, 0.1), transparent 28rem),
    linear-gradient(180deg, #090b10 0%, #07080b 44%, #050608 100%);
  color: #f5f7fb;
  font-family:
    Inter,
    ui-sans-serif,
    system-ui,
    -apple-system,
    BlinkMacSystemFont,
    'Segoe UI',
    sans-serif;
  line-height: 1.5;
  --v2-bg: #07080b;
  --v2-panel-3: #20242f;
  --v2-line: rgba(255, 255, 255, 0.1);
  --v2-text: #f5f7fb;
  --v2-muted: #9ba3b4;
  --v2-soft: #c8ceda;
  --v2-green: #45e68a;
  --v2-green-soft: rgba(69, 230, 138, 0.14);
  --v2-cyan: #50d7ff;
  --v2-cyan-soft: rgba(80, 215, 255, 0.12);
  --v2-shadow: 0 24px 80px rgba(0, 0, 0, 0.36);
}

.home-v2 *,
.home-v2 *::before,
.home-v2 *::after {
  box-sizing: border-box;
}

.home-v2 a {
  color: inherit;
  text-decoration: none;
}

.shell {
  width: min(1360px, calc(100% - 40px));
  margin: 0 auto;
}

.button,
.eyebrow {
  display: flex;
  align-items: center;
}

.button {
  justify-content: center;
  min-height: 40px;
  padding: 0 16px;
  border: 1px solid var(--v2-line);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.03);
  color: var(--v2-text);
  font-size: 0.92rem;
  font-weight: 700;
  cursor: pointer;
}

.button.primary {
  border-color: rgba(69, 230, 138, 0.58);
  background: var(--v2-green);
  color: #041008;
  box-shadow: 0 16px 44px rgba(69, 230, 138, 0.18);
}

.hero {
  padding: 88px 0 58px;
}

.hero-grid {
  display: grid;
  grid-template-columns: minmax(0, 0.84fr) minmax(0, 1.16fr);
  gap: 44px;
  align-items: start;
}

.eyebrow {
  width: fit-content;
  max-width: 100%;
  gap: 8px;
  padding: 6px 10px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.82rem;
  font-weight: 700;
}

h1 {
  width: auto;
  max-width: 520px;
  margin: 18px 0;
  font-size: clamp(3rem, 5.2vw, 5.5rem);
  line-height: 0.92;
  letter-spacing: -0.06em;
}

h1 span {
  display: block;
  white-space: normal;
}

.hero-copy {
  max-width: 650px;
  margin: 0 0 28px;
  color: var(--v2-soft);
  font-size: clamp(1.08rem, 1.8vw, 1.28rem);
}

.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 22px;
}

.hero-showcase {
  position: relative;
  display: grid;
  gap: 14px;
  min-width: 0;
}

.hero-showcase::before {
  content: '';
  position: absolute;
  inset: -34px -22px auto auto;
  z-index: -1;
  width: 78%;
  height: 70%;
  border-radius: 18px;
  background: linear-gradient(135deg, rgba(80, 215, 255, 0.2), rgba(69, 230, 138, 0.12));
  filter: blur(12px);
}

.architecture-section {
  padding-top: 12px;
}

.ai-workflow-section {
  padding: 12px 0 0;
}

.ai-workflow-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.05fr) minmax(320px, 0.95fr);
  gap: 22px;
}

.ai-workflow-copy,
.ai-steps-card {
  padding: 24px;
  border: 1px solid var(--v2-line);
  border-radius: 20px;
  background: rgba(255, 255, 255, 0.025);
}

.ai-workflow-copy h2 {
  margin: 0;
  font-size: clamp(2rem, 4vw, 3.1rem);
  line-height: 0.98;
  letter-spacing: -0.05em;
}

.ai-workflow-lead {
  margin: 18px 0 0;
  color: var(--v2-muted);
  line-height: 1.7;
}

.ai-workflow-note {
  margin: 14px 0 0;
  color: var(--v2-text);
  font-weight: 700;
  line-height: 1.6;
}

.ai-workflow-actions {
  display: flex;
  gap: 12px;
  margin-top: 18px;
  flex-wrap: wrap;
  align-items: center;
}

.ai-workflow-link {
  color: var(--v2-cyan);
  font-weight: 700;
}

.ai-workflow-link:hover {
  color: var(--v2-text);
}

.architecture-intro {
  display: grid;
  grid-template-columns: minmax(0, 0.95fr) minmax(320px, 0.65fr);
  gap: 32px;
  align-items: end;
  margin-bottom: 28px;
}

.architecture-intro h2 {
  max-width: 820px;
  margin: 0;
  font-size: clamp(2.25rem, 5vw, 4.5rem);
  line-height: 0.96;
  letter-spacing: -0.06em;
}

.path-cards {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 14px;
  margin-top: 26px;
}

.path-card {
  padding: 18px;
  border: 1px solid var(--v2-line);
  border-radius: 18px;
  background: rgba(255, 255, 255, 0.035);
}

.path-card.frontend {
  border-color: rgba(80, 215, 255, 0.24);
}

.path-card.backend {
  border-color: rgba(69, 230, 138, 0.3);
}

.path-card span {
  display: block;
  margin-bottom: 8px;
  color: var(--v2-muted);
  font-size: 0.78rem;
  font-weight: 900;
  letter-spacing: 0.11em;
  text-transform: uppercase;
}

.path-card strong {
  display: block;
  color: var(--v2-text);
  font-size: 1.1rem;
  letter-spacing: -0.025em;
}

section {
  padding: 62px 0;
}

.terminal-shell {
  overflow: hidden;
  border: 1px solid rgba(69, 230, 138, 0.28);
  border-radius: 28px;
  background:
    linear-gradient(180deg, rgba(69, 230, 138, 0.08), rgba(255, 255, 255, 0.025)),
    rgba(8, 13, 19, 0.88);
  box-shadow: 0 34px 100px rgba(0, 0, 0, 0.42);
}

.terminal-top {
  display: flex;
  height: 54px;
  align-items: center;
  gap: 9px;
  padding: 0 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(255, 255, 255, 0.035);
}

.terminal-dot {
  width: 11px;
  height: 11px;
  border-radius: 999px;
  background: #ff695c;
}

.terminal-dot:nth-child(2) {
  background: #ffd166;
}

.terminal-dot:nth-child(3) {
  background: #43ec8d;
}

.terminal-title {
  margin-left: 8px;
  color: #7890a4;
  font-size: 13px;
  font-weight: 800;
  letter-spacing: 0.02em;
}

.terminal-body {
  margin: 26px;
  padding: 24px;
  overflow-x: auto;
  border: 1px solid rgba(69, 230, 138, 0.18);
  border-radius: 20px;
  background: #020507;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, 'Liberation Mono', monospace;
  font-size: clamp(13px, 1.55vw, 16px);
  line-height: 1.78;
}

.terminal-comment {
  color: #7d9ab2;
}

.studio-caption {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
  padding: 0 26px 26px;
}

.caption-card {
  min-height: 88px;
  padding: 16px;
  border: 1px solid var(--v2-line);
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.035);
}

.caption-card strong {
  display: block;
  margin-bottom: 6px;
  color: var(--v2-text);
  font-size: 0.96rem;
}

.caption-card span {
  color: var(--v2-muted);
  font-size: 0.86rem;
  line-height: 1.4;
}

.runners-copy,
.runners-panel {
  box-shadow: var(--v2-shadow);
}

.runners-copy {
  padding: 28px;
  border: 1px solid var(--v2-line);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.runners-copy h2 {
  margin: 0 0 12px;
  font-size: clamp(2rem, 4vw, 3.8rem);
  line-height: 0.98;
  letter-spacing: -0.05em;
}

.runners-copy p,
.runners-panel-header p,
.runner-item p {
  margin: 0;
  color: var(--v2-muted);
}

.runner-chip-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 22px;
}

.runner-chip {
  padding: 8px 12px;
  border: 1px solid rgba(69, 230, 138, 0.2);
  border-radius: 999px;
  background: rgba(69, 230, 138, 0.08);
  color: #dff7ea;
  font-size: 0.9rem;
  font-weight: 700;
}

.runners-band {
  display: grid;
  grid-template-columns: 0.9fr 1.1fr;
  gap: 18px;
  align-items: stretch;
}

.runners-panel {
  overflow: hidden;
  border: 1px solid var(--v2-line);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.runners-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 20px;
  border-bottom: 1px solid var(--v2-line);
}

.runners-panel-header h3 {
  margin: 0;
}

.runners-list {
  display: grid;
  gap: 12px;
  padding: 20px;
}

.runner-item {
  padding: 16px 18px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 14px;
  background: rgba(16, 18, 24, 0.96);
}

.runner-item strong {
  display: block;
  margin-bottom: 6px;
  color: var(--v2-text);
  font-size: 1.02rem;
}

.status {
  padding: 4px 8px;
  border-radius: 999px;
  background: var(--v2-green-soft);
  color: var(--v2-green);
  font-size: 0.75rem;
  font-weight: 800;
}

.cta {
  padding: 72px 0 92px;
}

.cta-box {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 28px;
  align-items: center;
  padding: 30px;
  border: 1px solid rgba(69, 230, 138, 0.28);
  border-radius: 8px;
  background:
    linear-gradient(135deg, rgba(69, 230, 138, 0.1), rgba(80, 215, 255, 0.05)),
    rgba(255, 255, 255, 0.04);
  box-shadow: var(--v2-shadow);
}

.cta-copy {
  min-width: 0;
}

.cta-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
}

.cta h2 {
  margin: 0 0 8px;
  font-size: clamp(1.8rem, 3vw, 3rem);
  letter-spacing: -0.04em;
}

.cta p {
  margin: 0;
  color: var(--v2-muted);
}

@media (max-width: 980px) {
  .hero-grid,
  .ai-workflow-grid,
  .runners-band,
  .cta-box {
    grid-template-columns: 1fr;
  }

  h1 {
    max-width: 690px;
  }

  .architecture-intro {
    grid-template-columns: 1fr;
    align-items: start;
  }

  .path-cards {
    grid-template-columns: 1fr;
  }

  .terminal-body {
    margin: 18px;
    padding: 18px;
  }

  .studio-caption {
    grid-template-columns: 1fr;
    padding: 0 18px 18px;
  }
}

@media (max-width: 640px) {
  .shell {
    width: min(100% - 28px, 1180px);
  }

  .hero {
    padding-top: 54px;
  }

  h1 span {
    white-space: normal;
  }

  .cta-box {
    padding: 18px;
  }

  .runners-panel-header {
    grid-template-columns: 1fr;
  }

  .path-cards {
    grid-template-columns: 1fr;
  }
}
</style>
