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
    description: 'Best fit when you already have .NET 9 installed and want the easiest install/update path for both humans and AI tools.',
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

const genericStarterPrompt = `Use the local generated Automaton guidance in this folder if it exists; otherwise read ${publicAppConfig.llmsUrl}. Build this as a frontend-only Automaton app for: [describe the app]. Prefer plain HTML/CSS/JS with root-level files unless I ask for a framework. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, deploy with automaton deploy, and ask before publish. Only ask me to step in for authentication or publish approval.`

const existingAppStarterPrompt = `Use the local generated Automaton guidance in this folder if it exists; otherwise read ${publicAppConfig.llmsUrl}. I already have an Automaton app. Use the Automaton CLI as the only execution path: start with automaton setup, link this folder to app id [app-id] with automaton link, scaffold or update the app locally, deploy with automaton deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.`

const frontendOnlyStarterPrompt = `Use the local generated Automaton guidance in this folder if it exists; otherwise read ${publicAppConfig.llmsUrl}. Build this as a frontend-only Automaton app for: [describe the app]. Prefer plain HTML, CSS, and JavaScript with root-level files unless I explicitly ask for a framework or split source structure. Do not default to frontend/ for plain apps. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, deploy with automaton deploy, and ask before publish. Only ask me to step in for authentication or publish approval.`

const frameworkStarterPrompt = `Use the local generated Automaton guidance in this folder if it exists; otherwise read ${publicAppConfig.llmsUrl}. Build this as an Automaton app using [React or Vue]. Keep the source code in the repo. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, scaffold the app, make sure build and deployDir are correct, deploy with automaton deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.`

const quickStartSteps = [
  {
    title: 'Install CLI',
    detail: 'Start with the .NET global tool, or use the portable builds lower on this page if that fits your environment better.',
    code: 'dotnet tool install -g Automaton.Cli',
  },
  {
    title: 'Run setup',
    detail: 'Prepare the current machine or workspace and let the CLI sync local guidance automatically.',
    code: 'automaton setup',
  },
  {
    title: 'Paste the starter prompt',
    detail: 'After setup, ask the AI to build and deploy the app in the current folder.',
    code: genericStarterPrompt,
  },
  {
    title: 'Preview in Studio',
    detail: 'The AI should handle deploy; you use Studio preview and logs as the test loop.',
    code: 'Automaton Studio preview\nAutomaton Studio logs',
  },
]

const workflowSteps = [
  {
    title: 'Manual fallback',
    description: 'Use this only when you want to drive the CLI yourself instead of letting the AI handle the main flow.',
    code: 'automaton setup\nautomaton new "My App"\n# or\nautomaton link your-app-id\nautomaton deploy',
  },
  {
    title: 'Use Studio as the test loop',
    description: 'After deploy, preview the app and inspect logs in Automaton Studio before publish.',
    code: 'Automaton Studio preview\nAutomaton Studio logs',
  },
]

const starterPromptVariants = [
  {
    title: 'Generic starter',
    description: 'Best default when the user wants the AI to choose the simplest suitable stack.',
    code: genericStarterPrompt,
  },
  {
    title: 'Existing app',
    description: 'Use this when the remote Automaton app already exists and the folder should be linked first.',
    code: existingAppStarterPrompt,
  },
  {
    title: 'Frontend-only',
    description: 'Use this when the app should stay frontend-only unless backend code is truly necessary.',
    code: frontendOnlyStarterPrompt,
  },
  {
    title: 'React or Vue',
    description: 'Use this when the user explicitly wants a framework app.',
    code: frameworkStarterPrompt,
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
    detail: 'Deploy will select or prompt for a connected runner when Python controllers need one.',
    code: 'automaton deploy',
  },
  {
    title: 'Choose explicitly',
    detail: 'Pass a runner only when you already know which connected runner should handle backend deploy.',
    code: 'automaton deploy --runner your-runner-id',
  },
  {
    title: 'Deploy normally',
    detail: 'Reuse the same deploy command for every update.',
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
              Install the CLI, run <code>automaton setup</code>, paste one starter prompt into your AI tool,
              and let it build locally and deploy to Automaton without leaving your workflow.
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
              <p class="cli-card-subtitle">After setup, the AI should usually handle app creation, validation, and deploy. Use the CLI commands directly only as the manual fallback flow.</p>
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
            <h2 class="section-title cli-section-title">Recommended flow</h2>
            <p class="section-subtitle cli-section-subtitle">
              This is the default path most users should follow: install the CLI once, run setup, paste the starter prompt, and let the AI handle the rest.
            </p>
            <p class="section-subtitle cli-section-subtitle">
              Run <code>automaton setup</code> inside the app folder you want to create or manage. Each app should have its own <code>automaton.json</code> and local guidance scaffold.
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
            <h2 class="section-title cli-section-title">If you want more control</h2>
            <p class="section-subtitle cli-section-subtitle">
              Most users can stop at the recommended flow above. Use these fallback references only when you want to drive the CLI more directly.
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
            <h2 class="section-title cli-section-title">Starter prompt variants</h2>
            <p class="section-subtitle cli-section-subtitle">
              Start with the generic prompt below. Use the more specific variants only when the app already exists, should stay frontend-only, or must use a framework.
            </p>
          </div>

          <div class="workflow-grid">
            <article v-for="(prompt, index) in starterPromptVariants" :key="prompt.title" class="workflow-card">
              <div class="workflow-index">{{ index + 1 }}</div>
              <h3>{{ prompt.title }}</h3>
              <p>{{ prompt.description }}</p>
              <button class="copy-chip workflow-copy" @click="copyCode(`starter-${index}`, prompt.code)">
                <i :class="copiedKey === `starter-${index}` ? 'pi pi-check' : 'pi pi-copy'" />
                {{ copiedKey === `starter-${index}` ? 'Copied' : 'Copy' }}
              </button>
              <pre class="workflow-code"><code>{{ prompt.code }}</code></pre>
            </article>
          </div>
        </div>
      </section>

      <section class="cli-section cli-section-strong">
        <div class="container">
          <div class="cli-section-head">
            <h2 class="section-title cli-section-title">Only for backend apps</h2>
            <p class="section-subtitle cli-section-subtitle">
              Skip this entire section for frontend-only apps. Use it only when your app includes Python backend controllers.
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
              <p class="cli-card-subtitle">Use it when you want local development, Git control, and a reliable path from AI prompt to deploy.</p>
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
- Start with automaton setup
- Paste the starter prompt into your AI tool
- Let the AI handle create or link, validation, and deploy by default
- Use manual CLI commands only when you want the fallback flow
- Preview and inspect logs in Automaton Studio
- Reuse the same flow every time you update the app</code></pre>
            </article>
          </div>

          <div class="cli-card">
            <div class="cli-card-head">
              <span class="tag">Next step</span>
              <h2>Where to go deeper</h2>
              <p class="cli-card-subtitle">Keep this page simple. Use the public docs for the full workflow, and the AI instructions only when your coding tool needs them.</p>
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
