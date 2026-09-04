<script setup lang="ts">
import { ref } from 'vue'
import AppFooter from '@/components/AppFooter.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'

const installCommand = 'dotnet tool install -g Automaton.Cli'
const loginCommand = 'automaton login'
const setupCommand = 'automaton sync-ai'
const deployCommand = 'automaton deploy'
const publishCommand = 'automaton publish'
const setupFlow = [
  installCommand,
  loginCommand,
  '',
  'mkdir iss-tracker',
  'cd iss-tracker',
  '',
  '# Then ask your AI:',
  '# "Build a live ISS tracker with realtime orbital data."',
  '',
  deployCommand,
  '',
  publishCommand,
].join('\n')

const copiedKey = ref<string | null>(null)

async function copyCode(key: string, code: string) {
  try {
    await navigator.clipboard.writeText(code)
    copiedKey.value = key
    window.setTimeout(() => {
      if (copiedKey.value === key) {
        copiedKey.value = null
      }
    }, 1600)
  } catch {
    copiedKey.value = null
  }
}
</script>

<template>
  <div class="cli-concept-page">
    <PublicSiteHeader />

    <main>
      <header class="hero">
        <div class="container hero-grid">
          <div>
            <span class="eyebrow">AI-native deployment workflow</span>
            <h1>Your AI already writes code. Automaton ships it.</h1>
            <p class="hero-copy">
              Install the Automaton CLI, connect your AI coding workflow, and deploy real applications
              without manually wiring infrastructure.
            </p>

            <div class="hero-actions">
              <button class="button primary" type="button" @click="copyCode('install-command', installCommand)">
                {{ copiedKey === 'install-command' ? 'Copied install command' : 'Copy install command' }}
              </button>
              <a class="button" href="/docs/cli-ai-tools">Open documentation</a>
            </div>

            <p class="secondary-note">Use your own AI tools. Host your own runners. Keep your own code.</p>
          </div>

          <article class="terminal">
            <div class="terminal-bar">
              <div class="dots"><span /><span /><span /></div>
              <span class="copy-pill">Canonical workflow</span>
            </div>

            <div class="terminal-body">
              <div class="cmd">$ {{ installCommand }}</div>
              <div class="cmd">$ {{ loginCommand }}</div>
              <div class="cmd">$ {{ setupCommand }}</div>

              <div class="setup-card">
                <div class="check"><span class="tick">✓</span><span><strong>Authenticated</strong> with Automaton Studio</span></div>
                <div class="check"><span class="tick">✓</span><span><strong>Generated AI guidance</strong> for coding agents</span></div>
                <div class="check"><span class="tick">✓</span><span><strong>Prepared deployment workflow</strong> for publish and runners</span></div>
              </div>

              <div class="cmd"><span class="muted"># Then ask your AI:</span>
Build a live ISS tracker with Earth visualization and realtime orbital data.</div>

              <div class="cmd">$ {{ deployCommand }}</div>
            </div>
          </article>
        </div>
      </header>

      <section>
        <div class="container">
          <div class="section-head">
            <h2>Install once. Then just describe the app.</h2>
            <p>
              Setup teaches your AI how Automaton apps work. After that, you describe the product — the agent handles structure, deployment, and publishing.
            </p>
          </div>

          <div class="steps">
            <article class="step">
              <div class="step-number">1</div>
              <h3>Install the CLI</h3>
              <p>Use the .NET global tool to add Automaton commands to your local workflow.</p>
              <div class="step-snippet">dotnet tool install -g Automaton.Cli</div>
            </article>
            <article class="step">
              <div class="step-number">2</div>
              <h3>Run setup once</h3>
              <p>Connect your workspace and give your AI agent local guidance for building Automaton apps.</p>
              <div class="step-snippet">{{ setupCommand }}</div>
            </article>
            <article class="step">
              <div class="step-number">3</div>
              <h3>Ask for the app</h3>
              <p>Now the prompt becomes the product idea — not deployment instructions.</p>
              <div class="prompt-bubble">"Build a live ISS tracker with Earth visualization and realtime orbital data."</div>
            </article>
          </div>
        </div>
      </section>

      <section class="flow-band">
        <div class="container">
          <div class="section-head">
            <h2>The workflow layer between AI-generated code and deployed software.</h2>
            <p>Automaton CLI connects local AI tools, Automaton Studio, publishing, and optional server execution.</p>
          </div>

          <div class="flow">
            <div class="flow-item"><strong>AI Agent</strong><span>Generates and edits the app locally.</span></div>
            <div class="flow-item"><strong>Automaton CLI</strong><span>Applies structure, metadata, and deployment commands.</span></div>
            <div class="flow-item"><strong>Automaton Studio</strong><span>Manages apps, publishing, visibility, and remixes.</span></div>
            <div class="flow-item"><strong>Published App</strong><span>Goes live as public, private, or forkable.</span></div>
            <div class="flow-item"><strong>Runner</strong><span>Executes configured server logic locally or in Docker.</span></div>
          </div>
        </div>
      </section>

      <section>
        <div class="container split">
          <div>
            <div class="section-head">
              <h2>Quickstart workflow.</h2>
              <p>From local AI workflow to published app.</p>
            </div>

            <div class="code-panel">
              <pre>{{ setupFlow }}</pre>
            </div>
          </div>

          <div>
            <div class="section-head">
              <h2>Project structure</h2>
              <p>Automaton apps stay simple. Editable source and optional server execution are described by the app contract.</p>
            </div>

            <div class="code-panel">
              <pre>iss-tracker/
├─ AGENTS.md
├─ .automaton/
│  └─ app.json
├─ app-source/
└─ automaton.json</pre>
            </div>

            <p class="secondary-note">
              AGENTS.md gives coding agents local guidance. Server-side execution is optional and follows the app contract.
            </p>
          </div>
        </div>
      </section>

      <section>
        <div class="container">
          <div class="section-head">
            <h2>Built for modern AI coding workflows.</h2>
            <p>Automaton works with your preferred coding tools instead of replacing them.</p>
          </div>

          <div class="features">
            <article class="feature">
              <div class="feature-icon">CC</div>
              <h3>Claude Code</h3>
              <p>Prompt-driven local coding workflow with Automaton deployment support.</p>
            </article>
            <article class="feature">
              <div class="feature-icon">CX</div>
              <h3>Codex CLI</h3>
              <p>Generate and iterate locally while Automaton handles structure and publishing.</p>
            </article>
            <article class="feature">
              <div class="feature-icon">CU</div>
              <h3>Cursor</h3>
              <p>Use your preferred editor and AI coding environment without vendor lock-in.</p>
            </article>
            <article class="feature">
              <div class="feature-icon">GM</div>
              <h3>Gemini CLI</h3>
              <p>Connect prompt-driven coding workflows to deployable Automaton apps.</p>
            </article>
          </div>
        </div>
      </section>

      <section>
        <div class="container split">
          <article class="runner-card">
            <h2>Server runtimes need a Runner.</h2>
            <p>
              Static apps can deploy instantly. Apps with server logic execute through Automaton Runners.
            </p>
            <p class="secondary-note">Run them locally, on your own server, in Docker, or later using managed runners.</p>

            <div class="hero-actions">
              <a class="button" href="/docs/troubleshooting">Learn about runners</a>
            </div>
          </article>

          <div class="requirements">
            <div class="requirement"><span>Static apps</span><span>Requires HTTP runtime</span></div>
            <div class="requirement"><span>Server runtimes</span><span>Requires Runner</span></div>
            <div class="requirement"><span>Self-hosted Runner</span><span>Available today</span></div>
            <div class="requirement"><span>Managed Runner</span><span>Coming later</span></div>
          </div>
        </div>
      </section>

      <section class="cta">
        <div class="container cta-box">
          <div>
            <h2>Build and publish your first app in minutes.</h2>
            <p>Install the setup flow, tell your AI what to build, then deploy directly to Automaton Monster.</p>
          </div>

          <button class="button primary" type="button" @click="copyCode('setup-flow-bottom', setupFlow)">
            {{ copiedKey === 'setup-flow-bottom' ? 'Copied setup flow' : 'Copy setup flow' }}
          </button>
        </div>
      </section>
    </main>

    <AppFooter />
  </div>
</template>

<style scoped>
.cli-concept-page {
  background:
    radial-gradient(circle at 14% 0%, rgba(80, 215, 255, 0.13), transparent 31rem),
    radial-gradient(circle at 88% 8%, rgba(69, 230, 138, 0.11), transparent 30rem),
    linear-gradient(180deg, #090b10 0%, #07080b 46%, #050608 100%);
}

.hero {
  padding: 86px 0 54px;
}

.hero-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.15fr) minmax(380px, 0.85fr);
  gap: 40px;
  align-items: center;
}

.eyebrow {
  display: inline-flex;
  width: fit-content;
  padding: 6px 10px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.82rem;
  font-weight: 850;
}

h1 {
  margin: 18px 0;
  max-width: 980px;
  font-size: clamp(3.2rem, 7vw, 6.8rem);
  line-height: 0.92;
  letter-spacing: -0.06em;
  text-wrap: balance;
}

.hero-copy {
  max-width: 760px;
  margin: 0 0 26px;
  color: #c8ceda;
  font-size: clamp(1.08rem, 1.8vw, 1.24rem);
}

.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 22px;
}

.secondary-note {
  margin-top: 14px;
  color: #9ba3b4;
  font-size: 0.9rem;
}

.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 40px;
  padding: 0 16px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  color: #f5f7fb;
  font-weight: 850;
  font-size: 0.92rem;
  cursor: pointer;
  transition: background 0.2s ease, border-color 0.2s ease, transform 0.2s ease, box-shadow 0.2s ease;
}

.button:hover {
  border-color: rgba(255, 255, 255, 0.2);
  background: rgba(255, 255, 255, 0.065);
}

.button.primary {
  border-color: rgba(69, 230, 138, 0.55);
  background: #45e68a;
  color: #041008;
  box-shadow: 0 16px 44px rgba(69, 230, 138, 0.18);
}

.button.primary:hover {
  transform: translateY(-1px);
}

.terminal {
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.16);
  border-radius: 8px;
  background: rgba(13, 15, 21, 0.94);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.36);
}

.terminal-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 14px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  color: #9ba3b4;
  font-size: 0.82rem;
  font-weight: 800;
}

.dots {
  display: flex;
  gap: 6px;
}

.dots span {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: #1d2531;
}

.copy-pill {
  padding: 5px 9px;
  border: 1px solid rgba(69, 230, 138, 0.35);
  border-radius: 999px;
  background: rgba(69, 230, 138, 0.14);
  color: #45e68a;
  font-size: 0.72rem;
  font-weight: 950;
  text-transform: uppercase;
  cursor: pointer;
}

.terminal-body {
  display: grid;
  gap: 18px;
  padding: 24px;
  font-family: var(--font-mono);
  font-size: 0.97rem;
}

.cmd {
  color: #d9ffe8;
  white-space: pre-wrap;
}

.cmd .muted {
  color: #758096;
}

.setup-card {
  display: grid;
  gap: 12px;
  padding: 16px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.035);
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
}

.check {
  display: flex;
  align-items: center;
  gap: 10px;
  color: #c8ceda;
  font-size: 0.92rem;
}

.tick {
  display: grid;
  width: 22px;
  height: 22px;
  place-items: center;
  border-radius: 6px;
  background: rgba(69, 230, 138, 0.14);
  color: #45e68a;
  font-weight: 900;
}

section {
  padding: 62px 0;
}

.section-head {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 28px;
  margin-bottom: 24px;
}

.section-head h2 {
  margin: 0;
  max-width: 720px;
  font-size: clamp(2rem, 4vw, 3.5rem);
  line-height: 1;
  letter-spacing: -0.045em;
}

.section-head p {
  max-width: 440px;
  margin: 0;
  color: #9ba3b4;
}

.steps,
.features {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 14px;
}

.features {
  grid-template-columns: repeat(4, 1fr);
}

.step,
.feature {
  min-height: 210px;
  padding: 22px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.055), rgba(255, 255, 255, 0.025));
}

.step-number,
.feature-icon {
  display: grid;
  width: 38px;
  height: 38px;
  place-items: center;
  margin-bottom: 22px;
  border-radius: 8px;
  background: rgba(80, 215, 255, 0.12);
  color: #50d7ff;
  font-weight: 950;
}

.feature-icon {
  background: rgba(69, 230, 138, 0.14);
  color: #45e68a;
}

.step h3,
.feature h3 {
  margin: 0 0 9px;
  font-size: 1.16rem;
}

.step p,
.feature p {
  margin: 0;
  color: #9ba3b4;
}

.step-snippet,
.prompt-bubble {
  margin-top: 16px;
  padding: 10px 12px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.03);
  color: #aeb7c7;
  font-family: var(--font-mono);
  font-size: 0.8rem;
  line-height: 1.5;
}

.prompt-bubble {
  color: #d9ffe8;
}

.flow-band {
  border-top: 1px solid rgba(255, 255, 255, 0.06);
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.025), rgba(255, 255, 255, 0));
}

.flow {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 12px;
}

.flow-item {
  padding: 20px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.flow-item strong {
  display: block;
  margin-bottom: 6px;
}

.flow-item span {
  color: #9ba3b4;
  font-size: 0.9rem;
}

.code-panel {
  overflow: auto;
  padding: 24px;
  border: 1px solid rgba(255, 255, 255, 0.16);
  border-radius: 8px;
  background: rgba(13, 15, 21, 0.94);
  color: #d9ffe8;
  font-family: var(--font-mono);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.36);
}

.code-panel pre {
  margin: 0;
  white-space: pre-wrap;
}

.split {
  display: grid;
  grid-template-columns: 0.95fr 1.05fr;
  gap: 18px;
  align-items: start;
}

.runner-card {
  padding: 26px;
  border: 1px solid rgba(69, 230, 138, 0.25);
  border-radius: 8px;
  background: linear-gradient(135deg, rgba(69, 230, 138, 0.08), rgba(80, 215, 255, 0.04)), rgba(255, 255, 255, 0.04);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.36);
}

.runner-card h2 {
  margin: 0 0 12px;
  font-size: clamp(2rem, 4vw, 3.4rem);
  line-height: 1;
  letter-spacing: -0.045em;
}

.runner-card p {
  margin: 0;
  color: #9ba3b4;
}

.requirements {
  display: grid;
  gap: 10px;
}

.requirement {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 14px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  color: #c8ceda;
  font-weight: 850;
}

.requirement span:last-child {
  color: #9ba3b4;
  font-size: 0.86rem;
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
  background: linear-gradient(135deg, rgba(69, 230, 138, 0.1), rgba(80, 215, 255, 0.05)), rgba(255, 255, 255, 0.04);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.36);
}

.cta h2 {
  margin: 0 0 8px;
  font-size: clamp(1.8rem, 3vw, 3rem);
  letter-spacing: -0.04em;
}

.cta p {
  margin: 0;
  color: #9ba3b4;
}

@media (max-width: 980px) {
  .hero-grid,
  .split,
  .cta-box {
    grid-template-columns: 1fr;
  }

  .steps,
  .features,
  .flow {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 640px) {
  .hero {
    padding-top: 54px;
  }

  .terminal-body,
  .code-panel,
  .runner-card,
  .cta-box {
    padding: 18px;
  }

  .steps,
  .features,
  .flow {
    grid-template-columns: 1fr;
  }

  .section-head {
    display: grid;
  }
}
</style>
