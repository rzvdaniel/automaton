<script setup lang="ts">
import AppNav from '@/components/AppNav.vue'
import AppFooter from '@/components/AppFooter.vue'
import { ref } from 'vue'
import { publicAppConfig } from '@/config/publicAppConfig'

const activePath = ref<'editor' | 'agent' | 'cli' | 'mcp'>('agent')
const aiCliSetupPrompt = `"Read ${publicAppConfig.llmsUrl} first. Then install Automaton CLI in this environment if needed, or verify that it is already available."`
const copilotMcpConfig = `{
  "servers": {
    "automaton": {
      "type": "http",
      "url": "${publicAppConfig.mcpUrl}",
      "headers": {
        "X-API-Key": "your-api-key"
      }
    }
  }
}`
const claudeMcpCommand = `claude mcp add automaton --transport http "${publicAppConfig.mcpUrl}" --header "X-API-Key: your-api-key"`
</script>

<template>
  <div>
    <AppNav />
    <main class="docs-main">
      <div class="container">

        <!-- Page header -->
        <div class="docs-header">
          <h1 class="docs-title">Building apps with Automaton</h1>
          <p class="docs-subtitle">
            Automaton apps are frontend-agnostic static web apps with Python backends.
            Build with the tools you already like, then deploy to Automaton when ready.
          </p>
        </div>

        <!-- Path selector -->
        <div class="path-tabs">
          <button
            :class="['path-tab', activePath === 'editor' && 'active']"
            @click="activePath = 'editor'"
          >
            <i class="pi pi-desktop" />
            <div>
              <div class="tab-name">AI Editor</div>
              <div class="tab-desc">No setup · fastest start</div>
            </div>
          </button>
          <button
            :class="['path-tab', activePath === 'agent' && 'active']"
            @click="activePath = 'agent'"
          >
            <i class="pi pi-bolt" />
            <div>
              <div class="tab-name">AI + CLI</div>
              <div class="tab-desc">Minimal setup · AI-driven</div>
            </div>
          </button>
          <button
            :class="['path-tab', activePath === 'cli' && 'active']"
            @click="activePath = 'cli'"
          >
            <i class="pi pi-terminal" />
            <div>
              <div class="tab-name">CLI + Your Tools</div>
              <div class="tab-desc">Editors, coding tools</div>
            </div>
          </button>
          <button
            :class="['path-tab', activePath === 'mcp' && 'active']"
            @click="activePath = 'mcp'"
          >
            <i class="pi pi-sparkles" />
            <div>
              <div class="tab-name">MCP Server</div>
              <div class="tab-desc">Copilot Agent · Claude Code</div>
            </div>
          </button>
        </div>

        <!-- ── PATH 1: AI Editor ──────────────────────────────── -->
        <div v-if="activePath === 'editor'" class="path-content">
          <div class="path-intro">
            <span class="path-badge path-badge-green">Zero setup</span>
            <p>
              Open an app in the Automaton Studio editor, describe what you want in the AI chat,
              and watch it build. The AI writes the files, deploys the backend, and shows a live
              preview — no terminal required.
            </p>
          </div>

          <div class="steps">
            <div class="step">
              <div class="step-num">1</div>
              <div>
                <div class="step-title">Add your AI API key</div>
                <p class="step-body">
                  Go to <strong>Settings → AI Keys → Add Key</strong> and enter your key for
                  OpenAI, Anthropic, Google Gemini, or another supported provider.
                  The AI chat will not respond until at least one key is configured.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">2</div>
              <div>
                <div class="step-title">Create an app</div>
                <p class="step-body">Go to <strong>Apps → New App</strong>. Give it a name, then open it in the <strong>App Editor</strong>.</p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">3</div>
              <div>
                <div class="step-title">Describe what you want to build</div>
                <p class="step-body">
                  Type a prompt like <em>"Build a todo list with a Python backend that stores items
                  in SQLite"</em> in the AI chat panel. The AI will write all the files.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">4</div>
              <div>
                <div class="step-title">Deploy the backend</div>
                <p class="step-body">
                  Click <strong>Deploy Backend</strong> whenever Python backend files change.
                  Frontend-only changes take effect immediately without a deploy.
                  The live preview updates automatically.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">5</div>
              <div>
                <div class="step-title">Publish</div>
                <p class="step-body">
                  Click <strong>Publish</strong> in the Settings tab to get a public URL you can share.
                </p>
              </div>
            </div>
          </div>

          <div class="tip-box">
            <i class="pi pi-info-circle" />
            <span>
              The AI editor is best for quick prototypes and iterating on ideas.
              When your app grows or you want to use your own tools, switch to the CLI path.
            </span>
          </div>
        </div>

        <!-- ── PATH 2: AI + CLI ───────────────────────────────── -->
        <div v-if="activePath === 'agent'" class="path-content">
          <div class="path-intro">
            <span class="path-badge path-badge-amber">Minimal setup</span>
            <p>
              Use your own coding tool, give the AI access to the Automaton CLI, and let it drive
              the workflow in your local project. This is the best path when you want AI help, Git
              control, and a real folder on disk instead of building inside the Studio editor.
            </p>
          </div>

          <div class="steps">
            <div class="step">
              <div class="step-num">1</div>
              <div>
                <div class="step-title">Paste the starter prompt in your AI tool</div>
                <div class="prompt-example">
                  "Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as an Automaton app for: [describe the app]. Choose the simplest suitable frontend stack unless I explicitly ask for one. For plain HTML/CSS/JS apps, keep index.html, CSS, and JS at the project root by default instead of creating frontend/ unless I explicitly ask for a split source structure. Use the Automaton CLI as the primary execution path. Start with automaton setup. After that, handle the rest yourself: install the CLI if needed, create or link automaton.json in this folder, scaffold the app, run automaton validate, deploy, and ask before publish. Only ask me to step in for authentication or publish approval."
                </div>
                <p class="step-body">
                  This is the practical default for Codex, Copilot, Cursor, Claude Code, and similar tools.
                  The prompt already tells the AI to prefer local generated guidance after setup, use
                  <a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="docs-link">llms.txt</a> only as fallback,
                  install the CLI if needed, and start with <code>automaton setup</code>.
                </p>
                <p class="step-body">
                  Run <code>automaton setup</code> inside the app folder you want to create or manage.
                  Each app should have its own <code>automaton.json</code> and local guidance scaffold.
                </p>
                <p class="step-body">
                  If you need a prompt for an existing app, a frontend-only build, or a React/Vue-specific flow,
                  open <a :href="publicAppConfig.starterPromptsUrl" target="_blank" rel="noopener" class="docs-link">the starter prompts guide</a>.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">2</div>
              <div>
                <div class="step-title">Complete authentication if the AI asks for it</div>
                <p class="step-body">
                  If the AI runs <code>automaton setup</code> and prompts you for browser login or credentials, complete that step yourself.
                  After setup succeeds, the AI should handle most of the remaining workflow: create or link the app, generate files,
                  keep <code>automaton.json</code> correct, validate, and deploy.
                </p>
                <p class="step-body">
                  If you need a clean auth reset for testing or want to switch accounts, ask the AI to run
                  <code>automaton logout</code> or <code>automaton logout --all</code>
                  before starting the login flow again.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">3</div>
              <div>
                <div class="step-title">Use Studio as the test loop</div>
                <p class="step-body">
                  The AI should run validation and deploy commands itself, then use Automaton Studio as the current test loop:
                  open the app there, preview it there, inspect logs there, and iterate locally until the preview looks right.
                  It should only pause when the workflow needs you, such as interactive login or final publish approval.
                </p>
              </div>
            </div>
          </div>

          <div class="tip-box">
            <i class="pi pi-info-circle" />
            <span>
              This is the practical default for Copilot, Cursor, Claude Code, and similar tools when
              MCP is not connected. The AI builds locally, the CLI gives it a reliable deploy path,
              and the local generated guidance should usually be enough after setup. Use <a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="docs-link">llms.txt</a>
              only as the canonical fallback.
            </span>
          </div>
        </div>

        <!-- ── PATH 3: CLI ────────────────────────────────────── -->
        <div v-if="activePath === 'cli'" class="path-content">
          <div class="path-intro">
            <span class="path-badge path-badge-blue">For developers</span>
            <p>
              Build your app locally with any editor, then push it to Automaton with one command.
              This is the manual path when you want to write the code yourself and use the CLI mainly
              for linking, validation, deploy, and publish.
            </p>
          </div>

          <div class="steps">
            <div class="step">
              <div class="step-num">1</div>
              <div>
                <div class="step-title">Install the CLI</div>
                <div class="code-block">
                  <span class="code-prompt">$</span>
                  <span class="code-text">dotnet tool install -g Automaton.Cli</span>
                </div>
                <p class="step-body">Requires <a href="https://dot.net" target="_blank" rel="noopener" class="docs-link">.NET 9</a>. No .NET installed? Download a portable CLI build from <a href="/cli#downloads" class="docs-link">the CLI downloads page</a> instead.</p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">2</div>
              <div>
                <div class="step-title">Login and confirm identity</div>
                <div class="code-block code-block-multi"><pre>automaton setup
# optional reset for testing or account switching
automaton logout</pre></div>
                <p class="step-body">Prefer this over manual API key wiring when you want the CLI to drive the app workflow directly. Use <code>automaton logout</code> when you need to reset stored auth before testing a fresh setup flow or switching accounts.</p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">3</div>
              <div>
                <div class="step-title">Create or link the app</div>
                <div class="code-block code-block-multi"><pre>automaton new "My App"
# or
automaton link your-app-id</pre></div>
                <p class="step-body">
                  Use <code>new</code> for a new app in the current folder. Use <code>link</code>
                  only when the remote app already exists.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">4</div>
              <div>
                <div class="step-title">Build locally</div>
                <p class="step-body">
                  Keep your project in Git. Your frontend can be plain HTML/CSS/JS or any framework
                  that builds static files (React, Vue, Angular). <code>automaton.json</code> at the
                  project root links the folder to the app.
                </p>
                <p class="step-body">
                  Use this path when the implementation is primarily yours and the CLI is the delivery
                  layer. If you want the AI to drive the whole flow for you, use <strong>AI + CLI</strong>
                  instead.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">5</div>
              <div>
                <div class="step-title">Validate before deploy</div>
                <div class="code-block code-block-multi"><pre>automaton sync-ai   # only if generated guidance files are missing or stale
automaton validate
automaton status</pre></div>
                <p class="step-body">
                  Refresh generated guidance only when needed, then run the preflight check so missing config, missing remote app links, or missing
                  backend runner assignments fail before upload.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">6</div>
              <div>
                <div class="step-title">Handle backend runners when needed</div>
                <div class="code-block code-block-multi"><pre>automaton current-runner
automaton runners
automaton set-runner your-runner-id</pre></div>
                <p class="step-body">
                  Skip this if the app is frontend-only. If the app has Python backend controllers,
                  inspect the current assignment first, then pick an existing connected runner before the
                  first backend deploy. If no connected runner exists yet, create or deploy one in Automaton Studio.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">7</div>
              <div>
                <div class="step-title">Deploy to Studio, preview there, then publish when approved</div>
                <div class="code-block code-block-multi"><pre>automaton deploy
automaton publish --yes
automaton status</pre></div>
                <p class="step-body">
                  Uploads all files and pushes Python controllers to the assigned runner in one step.
                  After deploy, use Automaton Studio preview and logs as the current test/debug loop.
                  Run deploy again any time you make changes. Publish only after user approval, then
                  use <code>status</code> to confirm the live URL and current version.
                </p>
              </div>
            </div>
          </div>

          <div class="tip-box">
            <i class="pi pi-info-circle" />
            <span>
              <strong>Using Cursor or VS Code?</strong> The default flow is still simple: run <code>automaton setup</code>,
              then let the AI handle the project workflow. Local generated guidance should usually be enough after setup.
              Use <a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="docs-link">llms.txt</a>
              only as the canonical fallback when the tool does not honor local guidance files yet. If you want to drive the CLI manually,
              use the command reference and manual workflow guidance instead. For the full list of CLI commands, JSON output examples, and backend-runner flow, see the
              <strong>Documentation → Automaton CLI</strong> page inside Studio.
            </span>
          </div>
        </div>

        <!-- ── PATH 4: MCP ────────────────────────────────────── -->
        <div v-if="activePath === 'mcp'" class="path-content">
          <div class="path-intro">
            <span class="path-badge path-badge-purple">AI-native</span>
            <p>
              Connect Automaton as an MCP server so your AI coding assistant (GitHub Copilot Agent,
              Claude Code) can understand the required Automaton structure while you keep working in
              your local project. This is the most accurate path when your tool supports MCP. Use the
              Automaton CLI to upload and deploy the app.
            </p>
          </div>

          <div class="steps">
            <div class="step">
              <div class="step-num">1</div>
              <div>
                <div class="step-title">Get an API key</div>
                <p class="step-body">
                  In the Studio: <strong>API Keys → Generate New Key</strong>, scope: <code>execute</code>.
                  You'll get a key like <code>ak_execute_xxx</code>.
                </p>
              </div>
            </div>

            <div class="step">
              <div class="step-num">2</div>
              <div>
                <div class="step-title">Connect your tool</div>

                <div class="tool-tabs">
                  <div class="tool-section">
                    <div class="tool-label"><i class="pi pi-github" /> GitHub Copilot (VS Code)</div>
                    <p class="step-body">Create <code>.vscode/mcp.json</code> in your workspace:</p>
                    <div class="code-block code-block-multi">
<pre>{{ copilotMcpConfig }}</pre>
                    </div>
                    <p class="step-body">Then open Copilot Chat → switch to <strong>Agent mode</strong>.</p>
                  </div>

                  <div class="tool-section">
                    <div class="tool-label"><i class="pi pi-sparkles" /> Claude Code</div>
                    <div class="code-block">
                      <span class="code-prompt">$</span>
                      <span class="code-text">{{ claudeMcpCommand }}</span>
                    </div>
                    <p class="step-body">Then <code>cd</code> to your project and run <code>claude</code>.</p>
                  </div>
                </div>
              </div>
            </div>

            <div class="step">
              <div class="step-num">3</div>
              <div>
                <div class="step-title">Ask the AI to scaffold the project</div>
                <div class="prompt-example">
                  "Build this [idea] as an Automaton app."
                </div>
                <p class="step-body">
                  The AI can create or link the app, understand the required structure, and generate
                  the project in your local workspace. Keep the project in your own Git repo if you want version control.
                  The AI should usually handle the remaining deploy flow itself and only stop when it needs your help,
                  such as interactive auth, runner creation outside the CLI, or publish approval.
                </p>
              </div>
            </div>
          </div>

          <div class="tip-box">
            <i class="pi pi-info-circle" />
            <span>
              The AI learns the correct file structure from the MCP server's instructions.
              You still keep the project locally and use the CLI for deploy, but you usually do not
              need to re-explain the Automaton structure on every session.
            </span>
          </div>
        </div>

        <!-- ── Developer + AI guidance ────────────────────────── -->
        <div v-if="activePath === 'agent' || activePath === 'cli'" class="ref-section">
          <h2 class="ref-title">For developers and AI tools</h2>
          <p class="ref-subtitle">
            The public docs stay high-level. If you want the exact app structure, backend contract,
            starter prompts, and framework-specific guidance, use the AI-readable instruction files.
          </p>

          <div class="tip-box">
            <i class="pi pi-info-circle" />
            <span>
              <strong>Using Cursor or VS Code?</strong> The default flow is still simple: run <code>automaton setup</code>,
              then let the AI handle the project workflow. Local generated guidance should usually be enough after setup.
              Use <a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="docs-link">llms.txt</a>
              only as the canonical fallback when the tool does not honor local guidance files yet. If you want to drive the CLI manually,
              use the command reference and manual workflow guidance instead.
            </span>
          </div>

          <div class="tool-sections">
            <div class="tool-section">
              <ul class="link-list">
                <li><a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="docs-link">AI instructions</a></li>
                <li><a :href="publicAppConfig.cliInstallGuideUrl" target="_blank" rel="noopener" class="docs-link">CLI install and setup guide</a></li>
                <li><a :href="publicAppConfig.cliWorkflowGuideUrl" target="_blank" rel="noopener" class="docs-link">CLI workflow guide for coding tools</a></li>
                <li><a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="docs-link">llms.txt</a></li>
                <li><a :href="publicAppConfig.cliInstallGuideUrl" target="_blank" rel="noopener" class="docs-link">llms/automaton-cli-install.md</a></li>
                <li><a :href="publicAppConfig.cliWorkflowGuideUrl" target="_blank" rel="noopener" class="docs-link">llms/automaton-cli-deploy.md</a></li>
                <li><a :href="publicAppConfig.appsOverviewUrl" target="_blank" rel="noopener" class="docs-link">llms/automaton-apps-overview.md</a></li>
                <li><a :href="publicAppConfig.frontendStructureUrl" target="_blank" rel="noopener" class="docs-link">llms/automaton-frontend-structure.md</a></li>
                <li><a :href="publicAppConfig.backendControllersUrl" target="_blank" rel="noopener" class="docs-link">llms/automaton-backend-controllers.md</a></li>
                <li><a :href="publicAppConfig.starterPromptsUrl" target="_blank" rel="noopener" class="docs-link">llms/automaton-starter-prompts.md</a></li>
              </ul>
            </div>
          </div>

          <div class="tip-box">
            <i class="pi pi-info-circle" />
            <span>
              MCP is still the most accurate option when supported, but the
              <a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="docs-link">llms.txt</a>
              files give external AI tools a reliable fallback without overwhelming non-technical users
              on the public website.
            </span>
          </div>
        </div>

      </div>
    </main>
    <AppFooter />
  </div>
</template>

<style scoped>
.docs-main {
  padding: 64px 0 80px;
  min-height: 60vh;
}

.docs-header {
  max-width: 680px;
  margin-bottom: 40px;
}

.docs-title {
  font-size: 2.4rem;
  font-weight: 800;
  line-height: 1.15;
  margin: 0 0 12px;
}

.docs-subtitle {
  color: var(--color-text-muted);
  font-size: 1.05rem;
  line-height: 1.6;
  margin: 0;
}

.docs-resource-grid {
  margin-bottom: 20px;
}

/* ── Path tabs ── */
.path-tabs {
  display: flex;
  gap: 12px;
  margin-bottom: 32px;
  flex-wrap: wrap;
}

.path-tab {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 20px;
  border-radius: 10px;
  border: 1px solid rgba(255,255,255,0.1);
  background: rgba(255,255,255,0.03);
  color: var(--color-text);
  cursor: pointer;
  transition: all 0.18s ease;
  text-align: left;
  min-width: 180px;
}

.path-tab:hover {
  border-color: rgba(151,111,255,0.4);
  background: rgba(151,111,255,0.06);
}

.path-tab.active {
  border-color: rgba(151,111,255,0.7);
  background: rgba(151,111,255,0.12);
}

.path-tab i {
  font-size: 1.4rem;
  color: #976fff;
  flex-shrink: 0;
}

.tab-name {
  font-weight: 600;
  font-size: 0.95rem;
}

.tab-desc {
  font-size: 0.78rem;
  color: var(--color-text-muted);
  margin-top: 2px;
}

/* ── Path content ── */
.path-content {
  max-width: 760px;
}

.path-intro {
  margin-bottom: 28px;
}

.path-intro p {
  margin: 10px 0 0;
  color: var(--color-text-muted);
  line-height: 1.65;
  font-size: 0.97rem;
}

.path-badge {
  display: inline-block;
  padding: 3px 10px;
  border-radius: 20px;
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.03em;
  text-transform: uppercase;
}

.path-badge-green  { background: rgba(34,197,94,0.15); color: #4ade80; }
.path-badge-blue   { background: rgba(59,130,246,0.15); color: #60a5fa; }
.path-badge-purple { background: rgba(151,111,255,0.18); color: #b09eff; }

/* ── Steps ── */
.steps {
  display: flex;
  flex-direction: column;
  gap: 20px;
  margin-bottom: 28px;
}

.step {
  display: flex;
  gap: 16px;
  align-items: flex-start;
}

.step-num {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: rgba(151,111,255,0.2);
  border: 1px solid rgba(151,111,255,0.5);
  color: #b09eff;
  font-size: 0.8rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-top: 2px;
}

.step-title {
  font-weight: 600;
  font-size: 0.97rem;
  margin-bottom: 6px;
}

.step-body {
  color: var(--color-text-muted);
  font-size: 0.88rem;
  line-height: 1.6;
  margin: 6px 0 0;
}

.step-body code, code {
  background: rgba(255,255,255,0.08);
  padding: 1px 5px;
  border-radius: 4px;
  font-family: var(--font-mono);
  font-size: 0.85em;
}

.docs-link {
  color: #8ab4ff;
  text-decoration: underline;
  text-underline-offset: 0.14em;
}

.docs-link:hover {
  color: #b7ccff;
}

/* ── Code blocks ── */
.code-block {
  display: flex;
  align-items: center;
  gap: 10px;
  background: rgba(9,11,18,0.88);
  border: 1px solid rgba(255,255,255,0.1);
  border-radius: 8px;
  padding: 12px 16px;
  font-family: var(--font-mono);
  font-size: 0.85rem;
  margin: 8px 0;
  overflow-x: auto;
}

.code-block-multi {
  display: block;
  padding: 16px;
}

.code-block-multi pre {
  margin: 0;
  font-family: var(--font-mono);
  font-size: 0.83rem;
  line-height: 1.6;
  color: #c9d1d9;
  white-space: pre;
  overflow-x: auto;
}

.code-prompt {
  color: #888;
  flex-shrink: 0;
}

.code-text {
  color: #8ef0a1;
}

/* ── Tool sections ── */
.tool-sections { display: flex; flex-direction: column; gap: 20px; }
.tool-section { margin-top: 12px; }

.link-list {
  margin: 0;
  padding-left: 1.65rem;
}

.link-list li + li {
  margin-top: 0.55rem;
}

/* ── Prompt example ── */
.prompt-example {
  background: rgba(151,111,255,0.08);
  border: 1px solid rgba(151,111,255,0.25);
  border-left: 3px solid #976fff;
  border-radius: 6px;
  padding: 12px 16px;
  font-style: italic;
  color: #c9d1d9;
  font-size: 0.88rem;
  line-height: 1.55;
  margin: 8px 0;
}

/* ── Tip box ── */
.tip-box {
  display: flex;
  gap: 10px;
  align-items: flex-start;
  background: rgba(59,130,246,0.06);
  border: 1px solid rgba(59,130,246,0.2);
  border-radius: 8px;
  padding: 14px 16px;
  font-size: 0.875rem;
  color: var(--color-text-muted);
  line-height: 1.55;
}

.tip-box i {
  color: #60a5fa;
  margin-top: 1px;
  flex-shrink: 0;
}

/* ── Reference sections ── */
.ref-section {
  margin-top: 40px;
  padding-top: 20px;
  border-top: 1px solid rgba(255,255,255,0.07);
  max-width: 760px;
}

.ref-title {
  font-size: 1.45rem;
  font-weight: 700;
  margin: 0 0 8px;
}

.ref-subtitle {
  color: var(--color-text-muted);
  font-size: 0.9rem;
  margin: 0 0 14px;
}

.ref-section .tip-box {
  margin-top: 24px;
}

.ref-rule {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin-top: 14px;
  padding: 12px 14px;
  background: rgba(234,179,8,0.06);
  border: 1px solid rgba(234,179,8,0.2);
  border-radius: 8px;
  font-size: 0.86rem;
  color: var(--color-text-muted);
  line-height: 1.5;
}

.ref-rule i {
  color: #facc15;
  flex-shrink: 0;
  margin-top: 1px;
}

@media (max-width: 640px) {
  .docs-title { font-size: 1.8rem; }
  .path-tabs { flex-direction: column; }
  .path-tab { min-width: unset; }
}
</style>

