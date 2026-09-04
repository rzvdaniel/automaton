<script setup lang="ts">
// Server runtime section
</script>

<template>
  <section id="backend" class="backend-section">
    <div class="container">
      <div class="backend-inner">
        <div class="backend-text">
          <span class="section-label">
            <i class="pi pi-database" />
            Server runtime
          </span>
          <h2 class="section-title">Server runtime, simplified</h2>
          <p class="section-subtitle">
            Add server-side code when your app needs it.
            Automaton handles the configured runtime.
          </p>

          <ul class="backend-steps">
            <li>
              <span class="step-num">1</span>
              <div>
                <strong>Configure a server runtime</strong>
              </div>
            </li>
            <li>
              <span class="step-num">2</span>
              <div>
                <strong>Expose the routes your app needs</strong>
              </div>
            </li>
            <li>
              <span class="step-num">3</span>
              <div>
                <strong>Deploy — it just works</strong>
              </div>
            </li>
          </ul>
        </div>

        <div class="backend-code-wrap">
          <div class="code-block">
            <div class="code-header">
              <span class="code-file">server entry point</span>
              <span class="code-lang">Configured runtime</span>
            </div>
            <pre class="code-body">command: start-server
port: $AUTOMATON_PORT
health: /health</pre>
          </div>

          <div class="code-block" style="margin-top: 16px">
            <div class="code-header">
              <span class="code-file">client request</span>
              <span class="code-lang">JavaScript</span>
            </div>
            <pre class="code-body"><span class="fn">fetch</span>(<span class="str">"/your-route"</span>)
  .<span class="fn">then</span>(res => res.<span class="fn">json</span>())
  .<span class="fn">then</span>(data => {
    document.body.innerHTML = data.message
  })</pre>
          </div>

          <div class="structure-hint">
            <div class="structure-header">
              <i class="pi pi-folder" />
              Project structure
            </div>
            <div class="structure-tree">
              <div class="tree-item"><span class="tree-dir">my-app/</span></div>
              <div class="tree-item indent"><span class="tree-dir">app-source/</span> <span class="tree-note">editable source</span></div>
              <div class="tree-item indent"><span class="tree-dir">runtime/</span> <span class="tree-note">optional server code</span></div>
              <div class="tree-item indent"><span class="tree-file">automaton.json</span></div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.backend-section {
  padding: 100px 0;
  border-top: 1px solid var(--color-border);
}

.backend-inner {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 64px;
  align-items: start;
}

.backend-text .section-subtitle {
  margin-top: 12px;
  margin-bottom: 32px;
}

.backend-text .section-subtitle code,
.backend-steps code {
  font-family: var(--font-mono);
  font-size: 0.88em;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--color-surface-2);
  color: var(--color-blue);
  border: 1px solid var(--color-border);
}

.backend-steps {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.backend-steps li {
  display: flex;
  gap: 16px;
  align-items: flex-start;
}

.step-num {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: var(--color-accent-soft);
  border: 1px solid rgba(124, 106, 245, 0.3);
  color: var(--color-accent);
  font-size: 0.8rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-top: 2px;
}

.backend-steps li strong {
  font-size: 0.92rem;
  color: var(--color-text);
  font-weight: 600;
}

.backend-steps li p {
  margin-top: 4px;
  font-size: 0.84rem;
  color: var(--color-text-muted);
  line-height: 1.5;
}

/* Code blocks */
.code-block {
  border-radius: var(--radius);
  background: #0d0d14;
  border: 1px solid var(--color-border);
  overflow: hidden;
  font-family: var(--font-mono);
}

.code-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 16px;
  background: #161620;
  border-bottom: 1px solid var(--color-border);
  font-size: 0.78rem;
}

.code-file {
  color: var(--color-text-muted);
  font-weight: 600;
}

.code-lang {
  color: rgba(255, 255, 255, 0.3);
}

.code-body {
  padding: 16px 20px;
  font-size: 0.84rem;
  line-height: 1.7;
  color: var(--color-text);
  white-space: pre;
  overflow-x: auto;
}

.kw { color: var(--color-accent); }
.fn { color: var(--color-blue); }
.str { color: var(--color-green); }

/* Structure hint */
.structure-hint {
  margin-top: 16px;
  border-radius: var(--radius);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  overflow: hidden;
}

.structure-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  background: var(--color-surface-2);
  border-bottom: 1px solid var(--color-border);
  font-size: 0.8rem;
  color: var(--color-text-muted);
  font-weight: 600;
}

.structure-tree {
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.tree-item {
  font-family: var(--font-mono);
  font-size: 0.82rem;
  display: flex;
  align-items: center;
  gap: 8px;
}

.tree-item.indent {
  padding-left: 20px;
}

.tree-dir { color: var(--color-blue); }
.tree-file { color: var(--color-text-muted); }
.tree-note {
  font-size: 0.75rem;
  color: rgba(255, 255, 255, 0.25);
}

@media (max-width: 900px) {
  .backend-inner {
    grid-template-columns: 1fr;
    gap: 40px;
  }
}
</style>
