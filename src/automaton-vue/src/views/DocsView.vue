<script setup lang="ts">
import AppNav from '@/components/AppNav.vue'
import AppFooter from '@/components/AppFooter.vue'
import { ref } from 'vue'

const activePath = ref<'editor' | 'cli' | 'mcp'>('editor')
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
            Automaton apps are HTML/CSS/JavaScript frontends with optional Python backends.
            Pick the workflow that fits how you already work.
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
            :class="['path-tab', activePath === 'cli' && 'active']"
            @click="activePath = 'cli'"
          >
            <i class="pi pi-terminal" />
            <div>
              <div class="tab-name">CLI + Your IDE</div>
              <div class="tab-desc">Cursor, VS Code, any editor</div>
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
                <div class="step-title">Create an app</div>
                <p class="step-body">Go to <strong>Apps → New App</strong>. Give it a name.</p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">2</div>
              <div>
                <div class="step-title">Describe what you want to build</div>
                <p class="step-body">
                  Type a prompt like <em>"Build a todo list with a Python backend that stores items
                  in SQLite"</em> in the AI chat panel. The AI will write all the files.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">3</div>
              <div>
                <div class="step-title">Deploy the backend</div>
                <p class="step-body">
                  Click <strong>Deploy Backend</strong> to push the Python controllers to your runner.
                  The live preview updates automatically.
                </p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">4</div>
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

        <!-- ── PATH 2: CLI ────────────────────────────────────── -->
        <div v-if="activePath === 'cli'" class="path-content">
          <div class="path-intro">
            <span class="path-badge path-badge-blue">For developers</span>
            <p>
              Build your app locally with any editor, then push it to Automaton with one command.
              Works with Cursor, VS Code, WebStorm, or plain vim — whatever you prefer.
            </p>
          </div>

          <div class="steps">
            <div class="step">
              <div class="step-num">1</div>
              <div>
                <div class="step-title">Install the CLI</div>
                <div class="code-block">
                  <span class="code-prompt">$</span>
                  <span class="code-text">npm install -g @automaton/cli</span>
                </div>
              </div>
            </div>
            <div class="step">
              <div class="step-num">2</div>
              <div>
                <div class="step-title">Log in</div>
                <div class="code-block">
                  <span class="code-prompt">$</span>
                  <span class="code-text">automaton login --server https://api.automaton.monster --key your-api-key</span>
                </div>
                <p class="step-body">Get an API key from <strong>Settings → API Keys → New Key</strong> (scope: execute).</p>
              </div>
            </div>
            <div class="step">
              <div class="step-num">3</div>
              <div>
                <div class="step-title">Create an app from the Studio, then build locally</div>
                <p class="step-body">
                  Create the app in the Studio UI (or run <code>automaton apps create "My App"</code>).
                  An <code>automaton.json</code> file will appear in your project root — this links the folder to the app.
                </p>
                <div class="structure-block">
                  <div class="structure-line"><span class="file">automaton.json</span> <span class="comment">← created automatically</span></div>
                  <div class="structure-line"><span class="dir">frontend/</span></div>
                  <div class="structure-line indent"><span class="file">index.html</span> <span class="comment">← entry point (required)</span></div>
                  <div class="structure-line indent"><span class="dir">js/</span></div>
                  <div class="structure-line indent"><span class="dir">styles/</span></div>
                  <div class="structure-line"><span class="dir">backend/</span></div>
                  <div class="structure-line indent"><span class="file">todos.py</span> <span class="comment">← Python controller</span></div>
                  <div class="structure-line indent"><span class="file">requirements.txt</span> <span class="comment">← pip deps (optional)</span></div>
                </div>
              </div>
            </div>
            <div class="step">
              <div class="step-num">4</div>
              <div>
                <div class="step-title">Deploy</div>
                <div class="code-block">
                  <span class="code-prompt">$</span>
                  <span class="code-text">automaton deploy</span>
                </div>
                <p class="step-body">
                  Uploads all files and pushes Python controllers to the runner in one step.
                  Run it again any time you make changes.
                </p>
              </div>
            </div>
          </div>

          <div class="tip-box">
            <i class="pi pi-info-circle" />
            <span>
              <strong>Using Cursor or VS Code?</strong> Ask the AI to "build an Automaton app"
              and point it at this structure. The AI will write <code>frontend/index.html</code>
              and <code>backend/*.py</code>, then you run <code>automaton deploy</code> when ready.
            </span>
          </div>
        </div>

        <!-- ── PATH 3: MCP ────────────────────────────────────── -->
        <div v-if="activePath === 'mcp'" class="path-content">
          <div class="path-intro">
            <span class="path-badge path-badge-purple">AI-native</span>
            <p>
              Connect Automaton as an MCP server so your AI coding assistant (GitHub Copilot Agent,
              Claude Code) can write files <em>directly into Automaton</em> and deploy in one shot —
              no CLI step needed.
            </p>
          </div>

          <div class="steps">
            <div class="step">
              <div class="step-num">1</div>
              <div>
                <div class="step-title">Get an API key</div>
                <p class="step-body">
                  In the Studio: <strong>Settings → API Keys → New Key</strong>, scope: <code>execute</code>.
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
<pre>{
  "servers": {
    "automaton": {
      "type": "http",
      "url": "https://api.automaton.monster/api/mcp",
      "headers": {
        "X-API-Key": "your-api-key"
      }
    }
  }
}</pre>
                    </div>
                    <p class="step-body">Then open Copilot Chat → switch to <strong>Agent mode</strong>.</p>
                  </div>

                  <div class="tool-section">
                    <div class="tool-label"><i class="pi pi-sparkles" /> Claude Code</div>
                    <div class="code-block">
                      <span class="code-prompt">$</span>
                      <span class="code-text">claude mcp add automaton --transport http "https://api.automaton.monster/api/mcp" --header "X-API-Key: your-api-key"</span>
                    </div>
                    <p class="step-body">Then <code>cd</code> to your project and run <code>claude</code>.</p>
                  </div>
                </div>
              </div>
            </div>

            <div class="step">
              <div class="step-num">3</div>
              <div>
                <div class="step-title">Ask the AI to build and deploy</div>
                <div class="prompt-example">
                  "Build a leaderboard app. Store scores in a Python SQLite backend.
                  Use the automaton tools to write the files and deploy."
                </div>
                <p class="step-body">
                  The AI will call <code>create_app</code>, write all files, and call
                  <code>deploy_backend</code> — without you running any commands.
                  An <code>automaton.json</code> is written to your project so future sessions
                  continue working on the same app.
                </p>
              </div>
            </div>
          </div>

          <div class="tip-box">
            <i class="pi pi-info-circle" />
            <span>
              The AI learns the correct file structure from the MCP server's instructions.
              You don't need to explain <code>frontend/</code> or <code>backend/</code> — it already knows.
            </span>
          </div>
        </div>

        <!-- ── App Structure reference ────────────────────────── -->
        <div class="ref-section">
          <h2 class="ref-title">App structure reference</h2>
          <p class="ref-subtitle">All three paths produce the same structure on the server.</p>
          <div class="structure-block structure-block-large">
            <div class="structure-line"><span class="file">automaton.json</span> <span class="comment">← app ID + name (commit this)</span></div>
            <div class="structure-line"><span class="dir">frontend/</span></div>
            <div class="structure-line indent"><span class="file">index.html</span> <span class="comment">← required entry point</span></div>
            <div class="structure-line indent"><span class="file">*.css / *.js</span> <span class="comment">← any structure you like</span></div>
            <div class="structure-line indent"><span class="dir">pages/</span> <span class="comment">← additional HTML pages</span></div>
            <div class="structure-line"><span class="dir">backend/</span></div>
            <div class="structure-line indent"><span class="file">todos.py</span> <span class="comment">← filename = controller name</span></div>
            <div class="structure-line indent"><span class="file">requirements.txt</span> <span class="comment">← pip deps (optional)</span></div>
            <div class="structure-line indent"><span class="dir">shared/</span> <span class="comment">← shared Python modules</span></div>
            <div class="structure-line"><span class="dir">data/</span> <span class="comment">← SQLite databases (auto-created by runner)</span></div>
          </div>

          <div class="ref-rule">
            <i class="pi pi-exclamation-triangle" />
            All HTML/CSS/JS must be inside <code>frontend/</code>. The server serves <code>frontend/index.html</code> as the app entry point.
          </div>
        </div>

        <!-- ── Python backend quick reference ─────────────────── -->
        <div class="ref-section">
          <h2 class="ref-title">Python controller pattern</h2>
          <p class="ref-subtitle">One file per resource. Filename becomes the controller name.</p>
          <div class="code-block code-block-multi">
<pre># backend/todos.py

class TodosController:
    def list(self, inputs: dict) -> dict:
        # ... read from DB ...
        return {"success": True, "data": {"items": [...]}, "error": ""}

    def create(self, inputs: dict) -> dict:
        title = inputs.get("title", "").strip()
        if not title:
            return {"success": False, "data": None, "error": "title is required"}
        # ... insert into DB ...
        return {"success": True, "data": {"id": 1, "title": title}, "error": ""}

_controller = TodosController()

def handle(action_name: str, inputs: dict) -> dict:
    method = getattr(_controller, action_name, None)
    if method is None:
        return {"success": False, "data": None, "error": f"Unknown action '{action_name}'"}
    return method(inputs)</pre>
          </div>

          <p class="ref-subtitle" style="margin-top: 1rem">Calling it from the frontend:</p>
          <div class="code-block code-block-multi">
<pre>// frontend/js/api.js
const result = await window.automatonApi.callController('todos', 'list', {});
console.log(result.items);  // result is already the data object</pre>
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

/* ── Structure tree ── */
.structure-block {
  background: rgba(9,11,18,0.88);
  border: 1px solid rgba(255,255,255,0.08);
  border-radius: 8px;
  padding: 14px 16px;
  font-family: var(--font-mono);
  font-size: 0.82rem;
  line-height: 1.8;
  margin: 10px 0;
}

.structure-block-large {
  font-size: 0.85rem;
}

.structure-line {
  display: flex;
  gap: 10px;
}

.structure-line.indent {
  padding-left: 20px;
}

.file { color: #c9d1d9; }
.dir  { color: #79c0ff; }
.comment { color: #6a737d; }

/* ── Tool sections ── */
.tool-sections { display: flex; flex-direction: column; gap: 20px; }
.tool-section { margin-top: 12px; }
.tool-label {
  font-weight: 600;
  font-size: 0.88rem;
  color: #b09eff;
  margin-bottom: 8px;
  display: flex;
  align-items: center;
  gap: 6px;
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
  margin-top: 56px;
  padding-top: 48px;
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

