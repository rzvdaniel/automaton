<script setup lang="ts">
import { ref } from 'vue'
import AppNav from '@/components/AppNav.vue'
import AppFooter from '@/components/AppFooter.vue'
import { publicAppConfig } from '@/config/publicAppConfig'

const downloadCards = [
  {
    title: 'Windows x64',
    description: 'Portable zip with the self-contained CLI binary.',
    href: '/cli/automaton-cli-win-x64.zip',
    badge: 'zip',
  },
  {
    title: 'Linux x64',
    description: 'Portable tar.gz for Linux servers and developer machines.',
    href: '/cli/automaton-cli-linux-x64.tar.gz',
    badge: 'tar.gz',
  },
  {
    title: 'macOS Apple Silicon',
    description: 'Recommended direct build for Apple Silicon Macs.',
    href: '/cli/automaton-cli-osx-arm64.tar.gz',
    badge: 'tar.gz',
  },
]

const installModes = [
  {
    title: 'Recommended: .NET global tool',
    description: 'Best fit when you already have .NET installed and want automatic updates through NuGet.',
    code: 'dotnet tool install -g Automaton.Cli',
  },
]

const directScripts = [
  {
    title: 'PowerShell direct install',
    description: 'Download the Windows build, extract it, and run the CLI locally.',
    code: [
      '$release = "$HOME\\automaton-cli"',
      'New-Item -ItemType Directory -Force -Path $release | Out-Null',
      `Invoke-WebRequest ${publicAppConfig.cliDownloadsBaseUrl}automaton-cli-win-x64.zip -OutFile "$release\\automaton-cli-win-x64.zip"`,
      'Expand-Archive "$release\\automaton-cli-win-x64.zip" -DestinationPath $release -Force',
      '& "$release\\automaton.exe" --help',
    ].join('\n'),
  },
  {
    title: 'Bash direct install',
    description: 'Download and extract the Linux build to a local folder.',
    code: [
      'mkdir -p "$HOME/automaton-cli"',
      `curl -L ${publicAppConfig.cliDownloadsBaseUrl}automaton-cli-linux-x64.tar.gz -o "$HOME/automaton-cli/automaton-cli-linux-x64.tar.gz"`,
      'tar -xzf "$HOME/automaton-cli/automaton-cli-linux-x64.tar.gz" -C "$HOME/automaton-cli"',
      '"$HOME/automaton-cli/automaton" --help',
    ].join('\n'),
  },
]

const quickStartSteps = [
  {
    title: 'Install CLI',
    detail: 'Start with the .NET global tool, or use the portable builds further down this page.',
    code: 'dotnet tool install -g Automaton.Cli',
  },
  {
    title: 'Login',
    detail: 'Prefer CLI login and identity confirmation before creating or linking the app.',
    code: 'automaton login\nautomaton whoami',
  },
  {
    title: 'Create app',
    detail: 'Create a linked app in the current folder so automaton.json is written locally.',
    code: 'automaton create --name "My App"',
  },
  {
    title: 'Validate',
    detail: 'Run a cheap preflight check before upload so obvious config and runner issues fail early.',
    code: 'automaton validate',
  },
  {
    title: 'Deploy and preview in Studio',
    detail: 'Deploy from your local workflow, then use Studio preview and logs as the current test loop.',
    code: 'automaton deploy',
  },
]

const workflowSteps = [
  {
    title: 'Start with the AI-readable contract',
    description: 'Point your IDE or AI agent to llms.txt before asking it to generate files. It should create the app structure and automaton.json for you.',
    code: `Read ${publicAppConfig.llmsUrl}, then build this as an Automaton app for: [describe the app]. Use the Automaton CLI to deploy it.`,
  },
  {
    title: 'Connect the local project',
    description: 'Create a new linked app in the current folder, or link to an existing one when the remote app already exists.',
    code: 'automaton create --name "My App"\n# or\nautomaton link your-app-id',
  },
  {
    title: 'Deploy from your IDE',
    description: 'Run automaton validate, deploy from your local workflow, then preview the app and inspect logs in Automaton Studio before publish.',
    code: 'automaton deploy',
  },
]

const backendChecklist = [
  {
    title: 'Frontend-only app',
    detail: 'You can skip runner setup and deploy immediately.',
    code: 'automaton deploy',
  },
  {
    title: 'Backend app',
    detail: 'List your connected runners before the first backend deploy.',
    code: 'automaton runners',
  },
  {
    title: 'Assign once',
    detail: 'Pick one runner for the app so Python controllers have a target.',
    code: 'automaton set-runner your-runner-id',
  },
  {
    title: 'Deploy normally',
    detail: 'After assignment, reuse the same deploy command for every update.',
    code: 'automaton deploy',
  },
]

const copiedKey = ref<string | null>(null)

async function copyCode(key: string, code: string) {
  try {
    await navigator.clipboard.writeText(code)
    copiedKey.value = key
    window.setTimeout(() => {
      if (copiedKey.value === key) {
        copiedKey.value = null
      }
    }, 1500)
  } catch {
    copiedKey.value = null
  }
}
</script>

<template>
  <div class="cli-page">
    <AppNav />

    <main>
      <section class="cli-hero">
        <div class="container cli-hero-inner">
          <div class="cli-copy">
            <span class="section-label"><i class="pi pi-terminal" /> Automaton CLI</span>
            <h1 class="section-title cli-title">Install fast. Deploy from your editor. Stay in control.</h1>
            <p class="cli-subtitle">
              Use the NuGet package when you already have .NET. Use direct downloads when you want a
              portable binary. Use llms.txt and the CLI together when you want AI-assisted app builds
              without leaving your local workflow.
            </p>

            <div class="cli-hero-actions">
              <a href="#install" class="btn btn-primary">
                <i class="pi pi-download" />
                Install CLI
              </a>
              <a href="#workflow" class="btn btn-secondary">
                <i class="pi pi-compass" />
                View workflow
              </a>
              <a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="btn btn-secondary">
                <i class="pi pi-sparkles" />
                Open llms.txt
              </a>
            </div>
          </div>
        </div>
      </section>

      <section id="downloads" class="cli-section">
        <div class="container">
          <div class="cli-section-head">
            <h2 class="section-title cli-section-title">Downloads</h2>
            <p class="section-subtitle cli-section-subtitle">
              Three direct builds are bundled with the website image for the most common install targets.
              NuGet remains the recommended path for managed developer machines.
            </p>
          </div>

          <div class="downloads-grid">
            <a v-for="download in downloadCards" :key="download.title" :href="download.href" class="download-card">
              <div class="download-card-head">
                <h3>{{ download.title }}</h3>
                <span class="download-badge">{{ download.badge }}</span>
              </div>
              <p>{{ download.description }}</p>
              <span class="download-link">Download <i class="pi pi-arrow-right" /></span>
            </a>

            <a href="/cli/checksums.txt" class="download-card download-card-secondary">
              <div class="download-card-head">
                <h3>Checksums</h3>
                <span class="download-badge">sha256</span>
              </div>
              <p>Verify the integrity of the portable downloads before running them.</p>
              <span class="download-link">Open checksums <i class="pi pi-arrow-right" /></span>
            </a>
          </div>
        </div>
      </section>

      <section id="install" class="cli-section">
        <div class="container cli-stack">
          <div class="cli-card">
            <div class="cli-card-head">
              <span class="tag">Install</span>
              <h2>Install with NuGet</h2>
              <p class="cli-card-subtitle">If your AI follows llms.txt, automaton.json should be created automatically. Use init or link only as a manual fallback.</p>
            </div>

            <article v-for="mode in installModes" :key="mode.title" class="code-card">
              <div class="code-card-header">
                <div>
                  <h3>{{ mode.title }}</h3>
                  <p>{{ mode.description }}</p>
                </div>
                <button class="copy-chip" @click="copyCode(mode.title, mode.code)">
                  <i :class="copiedKey === mode.title ? 'pi pi-check' : 'pi pi-copy'" />
                  {{ copiedKey === mode.title ? 'Copied' : 'Copy' }}
                </button>
              </div>
              <pre class="code-card-body"><code>{{ mode.code }}</code></pre>
            </article>
          </div>

          <div id="scripts" class="cli-card">
            <div class="cli-card-head">
              <span class="tag">Scripts</span>
              <h2>Portable install scripts</h2>
            </div>

            <div class="script-grid">
              <article v-for="script in directScripts" :key="script.title" class="code-card script-card">
                <div class="code-card-header script-card-header">
                  <div>
                    <h3>{{ script.title }}</h3>
                    <p>{{ script.description }}</p>
                  </div>
                  <button class="copy-chip" @click="copyCode(script.title, script.code)">
                    <i :class="copiedKey === script.title ? 'pi pi-check' : 'pi pi-copy'" />
                    {{ copiedKey === script.title ? 'Copied' : 'Copy' }}
                  </button>
                </div>
                <pre class="code-card-body script-code"><code>{{ script.code }}</code></pre>
              </article>
            </div>
          </div>
        </div>
      </section>

      <section class="cli-section cli-section-strong">
        <div class="container">
          <div class="cli-section-head">
            <h2 class="section-title cli-section-title">Quick start</h2>
            <p class="section-subtitle cli-section-subtitle">
              The shortest path from install to Studio preview. For the full CLI + your tools flow, open the docs page.
            </p>
          </div>

          <div class="backend-checklist-grid">
            <article v-for="item in quickStartSteps" :key="item.title" class="backend-checklist-card">
              <h3>{{ item.title }}</h3>
              <p>{{ item.detail }}</p>
              <button class="copy-chip workflow-copy" @click="copyCode(`quickstart-${item.title}`, item.code)">
                <i :class="copiedKey === `quickstart-${item.title}` ? 'pi pi-check' : 'pi pi-copy'" />
                {{ copiedKey === `quickstart-${item.title}` ? 'Copied' : 'Copy' }}
              </button>
              <pre class="workflow-code"><code>{{ item.code }}</code></pre>
            </article>
          </div>

          <a href="/docs" class="cli-panel-link cli-quickstart-link">See detailed CLI + your tools docs <i class="pi pi-arrow-right" /></a>
        </div>
      </section>

      <section id="workflow" class="cli-section cli-section-strong">
        <div class="container">
          <div class="cli-section-head">
            <h2 class="section-title cli-section-title">CLI + AI workflow</h2>
            <p class="section-subtitle cli-section-subtitle">
              The easiest reliable path is to combine the CLI with the AI-readable project contract and your local IDE, then use Studio for preview and logs.
            </p>
          </div>

          <div class="workflow-grid">
            <article v-for="(step, index) in workflowSteps" :key="step.title" class="workflow-card">
              <div class="workflow-index">{{ index + 1 }}</div>
              <h3>{{ step.title }}</h3>
              <p>{{ step.description }}</p>
              <button class="copy-chip workflow-copy" @click="copyCode(`workflow-${index}`, step.code)">
                <i :class="copiedKey === `workflow-${index}` ? 'pi pi-check' : 'pi pi-copy'" />
                {{ copiedKey === `workflow-${index}` ? 'Copied' : 'Copy' }}
              </button>
              <pre class="workflow-code"><code>{{ step.code }}</code></pre>
            </article>
          </div>
        </div>
      </section>

      <section class="cli-section cli-section-strong">
        <div class="container">
          <div class="cli-section-head">
            <h2 class="section-title cli-section-title">Backend app checklist</h2>
            <p class="section-subtitle cli-section-subtitle">
              Only needed for apps that include Python backend controllers. Frontend-only apps can skip this section.
            </p>
          </div>

          <div class="backend-checklist-grid">
            <article v-for="item in backendChecklist" :key="item.title" class="backend-checklist-card">
              <h3>{{ item.title }}</h3>
              <p>{{ item.detail }}</p>
              <button class="copy-chip workflow-copy" @click="copyCode(`backend-${item.title}`, item.code)">
                <i :class="copiedKey === `backend-${item.title}` ? 'pi pi-check' : 'pi pi-copy'" />
                {{ copiedKey === `backend-${item.title}` ? 'Copied' : 'Copy' }}
              </button>
              <pre class="workflow-code"><code>{{ item.code }}</code></pre>
            </article>
          </div>
        </div>
      </section>

      <section class="cli-section cli-section-strong">
        <div class="container cli-grid-two">
          <div class="cli-card">
            <div class="cli-card-head">
              <span class="tag">Why CLI</span>
              <h2>What the CLI is for</h2>
              <p class="cli-card-subtitle">Use it when you want local development, Git control, and a clean path to validate, deploy, preview in Studio, and redeploy.</p>
            </div>

            <article class="code-card">
              <div class="code-card-header">
                <div>
                  <h3>Best fit</h3>
                  <p>The CLI is the bridge between your local project and Automaton hosting.</p>
                </div>
              </div>
              <pre class="code-card-body"><code>- Build locally with your own editor or AI tool
- Keep the project in your repo
- Run automaton validate before deploy
- Create or link the Automaton app
- Deploy frontend files and backend controllers
- Preview and inspect logs in Automaton Studio
- Reuse the same flow every time you update the app</code></pre>
            </article>
          </div>

          <div class="cli-card">
            <div class="cli-card-head">
              <span class="tag">Next step</span>
              <h2>Where to go deeper</h2>
              <p class="cli-card-subtitle">Keep this page high-level. Use the public docs for the full developer workflow, and the AI instructions only when your coding tool needs them.</p>
            </div>

            <article class="code-card">
              <div class="code-card-header code-card-header-stack">
                <div>
                  <h3>Detailed references</h3>
                  <p>Use the public docs for the end-to-end developer flow. If you are working with Copilot, Claude Code, or another coding agent, open the AI instructions as a secondary reference.</p>
                </div>
                <div class="resource-links">
                  <a href="/docs" class="btn btn-primary resource-button resource-button-primary">Open Building apps with Automaton</a>
                  <a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="btn btn-secondary resource-button resource-button-secondary">Open AI instructions</a>
                </div>
              </div>
            </article>
          </div>
        </div>
      </section>
    </main>

    <AppFooter />
  </div>
</template>

<style scoped>
.cli-hero {
  padding: 42px 0 24px;
  background:
    radial-gradient(circle at 0% 0%, rgba(124, 106, 245, 0.18), transparent 32%),
    radial-gradient(circle at 100% 20%, rgba(77, 166, 255, 0.12), transparent 26%);
}

.cli-hero-inner {
  display: block;
}

.cli-title {
  max-width: 1080px;
}

.cli-subtitle {
  max-width: 920px;
  margin-top: 14px;
  color: rgba(233, 236, 245, 0.78);
  font-size: 1.04rem;
}

.cli-hero-actions {
  display: flex;
  gap: 12px;
  margin-top: 26px;
  flex-wrap: wrap;
}

.cli-panel-link {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  margin-top: 16px;
  color: #d7d2ff;
  font-size: 0.88rem;
  font-weight: 600;
}

.cli-quickstart-link {
  margin-top: 18px;
}

.cli-section {
  padding: 22px 0 26px;
}

.cli-section-strong {
  padding-bottom: 44px;
}

.cli-section-head {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 20px;
}

.cli-section-title {
  font-size: 1.75rem;
}

.cli-section-subtitle {
  max-width: 760px;
}

.downloads-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
}

.download-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 18px;
  border-radius: 16px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  background: linear-gradient(180deg, rgba(18, 20, 31, 0.98), rgba(12, 14, 23, 0.98));
  transition: transform 0.2s ease, border-color 0.2s ease;
}

.download-card:hover {
  transform: translateY(-2px);
  border-color: rgba(124, 106, 245, 0.34);
}

.download-card-secondary {
  background: linear-gradient(180deg, rgba(15, 18, 27, 0.96), rgba(12, 15, 22, 0.96));
}

.download-card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.download-card-head h3 {
  font-size: 1rem;
}

.download-card p {
  color: rgba(233, 236, 245, 0.7);
  font-size: 0.88rem;
}

.download-badge {
  padding: 4px 10px;
  border-radius: 999px;
  background: rgba(124, 106, 245, 0.14);
  color: #d6ceff;
  font-size: 0.72rem;
  font-weight: 700;
}

.download-link {
  margin-top: auto;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 0.86rem;
  font-weight: 600;
  color: #ffffff;
}

.cli-grid-two {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18px;
}

.cli-stack {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.cli-card {
  padding: 20px;
  border-radius: 18px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  background: linear-gradient(180deg, rgba(16, 18, 28, 0.98), rgba(11, 13, 20, 0.98));
}

.cli-card-head {
  margin-bottom: 16px;
}

.cli-card-head h2 {
  margin-top: 10px;
  font-size: 1.3rem;
}

.cli-card-subtitle {
  margin-top: 6px;
  color: rgba(233, 236, 245, 0.68);
  font-size: 0.84rem;
}

.code-card {
  padding: 16px;
  border-radius: 14px;
  border: 1px solid rgba(255, 255, 255, 0.06);
  background: rgba(255, 255, 255, 0.02);
}


.script-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}

.script-card {
  height: 100%;
}

.script-card-header {
  flex-direction: column;
  align-items: flex-start;
}

.script-code {
  font-size: 0.74rem;
  line-height: 1.5;
}

.code-card-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 10px;
}

.code-card-header-stack {
  flex-direction: column;
  align-items: flex-start;
}

.resource-links {
  display: flex;
  flex-wrap: nowrap;
  align-items: center;
  gap: 10px;
  margin-top: 14px;
}

.resource-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  white-space: nowrap;
  padding: 10px 14px;
  font-size: 0.82rem;
  line-height: 1.2;
}

.resource-button-primary {
  flex: 1 1 auto;
}

.resource-button-secondary {
  flex: 0 0 auto;
  padding-inline: 12px;
}

.code-card-header h3 {
  font-size: 0.96rem;
}

.code-card-header p {
  margin-top: 4px;
  color: rgba(233, 236, 245, 0.68);
  font-size: 0.84rem;
}

.copy-chip {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border-radius: 999px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  background: rgba(255, 255, 255, 0.02);
  color: var(--color-text);
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
}

.code-card-body,
.workflow-code {
  margin: 0;
  padding: 14px;
  border-radius: 12px;
  background: #0d1019;
  border: 1px solid rgba(124, 106, 245, 0.12);
  color: #91f5b3;
  font-family: var(--font-mono);
  font-size: 0.8rem;
  line-height: 1.6;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-word;
}

.workflow-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.backend-checklist-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
}

.backend-checklist-card {
  padding: 18px;
  border-radius: 18px;
  border: 1px solid rgba(125, 211, 252, 0.16);
  background: linear-gradient(180deg, rgba(16, 21, 33, 0.98), rgba(10, 13, 21, 0.98));
}

.backend-checklist-card h3 {
  font-size: 1rem;
}

.backend-checklist-card p {
  margin: 8px 0 12px;
  color: rgba(233, 236, 245, 0.7);
  font-size: 0.86rem;
}

.workflow-card {
  position: relative;
  padding: 18px;
  border-radius: 18px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  background: linear-gradient(180deg, rgba(18, 20, 32, 0.98), rgba(12, 15, 24, 0.98));
}

.workflow-index {
  width: 34px;
  height: 34px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: #4f36b3;
  color: #fff;
  font-size: 0.86rem;
  font-weight: 700;
  margin-bottom: 12px;
}

.workflow-card h3 {
  font-size: 1rem;
}

.workflow-card p {
  margin: 8px 0 12px;
  color: rgba(233, 236, 245, 0.68);
  font-size: 0.86rem;
}

.workflow-copy {
  margin-bottom: 12px;
}

@media (max-width: 1100px) {
  .downloads-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .backend-checklist-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .workflow-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 900px) {
  .cli-grid-two {
    grid-template-columns: 1fr;
  }

  .script-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .downloads-grid {
    grid-template-columns: 1fr;
  }

  .backend-checklist-grid {
    grid-template-columns: 1fr;
  }

  .code-card-header {
    flex-direction: column;
  }

  .resource-links {
    flex-wrap: wrap;
  }

  .resource-button-primary,
  .resource-button-secondary {
    flex: 1 1 100%;
  }
}
</style>