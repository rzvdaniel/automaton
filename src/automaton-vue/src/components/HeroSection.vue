<script setup lang="ts">
// Hero section
</script>

<template>
  <section class="hero">
    <!-- Background glow -->
    <div class="hero-glow" aria-hidden="true" />

    <div class="container hero-inner">
      <!-- Left: text -->
      <div class="hero-text">
        <h1 class="hero-title">
          <span class="hero-title-line">Build with your tools.</span><br />
          <span class="hero-accent">Ship instantly.</span>
        </h1>

        <p class="hero-subtitle">
          Use GitHub Copilot, Claude, or your own stack.
          Automaton turns your code into a live, shareable app — no DevOps.
        </p>

        <div class="hero-actions">
          <a href="#install" class="btn btn-primary btn-lg">
            <i class="pi pi-download" />
            Install CLI
          </a>
          <a href="/apps" class="btn btn-secondary btn-lg">
            Explore apps
            <i class="pi pi-arrow-right" />
          </a>
        </div>

        <div class="hero-tools">
          <span class="tools-label">Works with</span>
          <div class="tools-list">
            <span class="tool-badge"><i class="pi pi-github" /> GitHub Copilot</span>
            <span class="tool-badge"><i class="pi pi-sparkles" /> Claude</span>
            <span class="tool-badge"><i class="pi pi-box" /> Cursor</span>
            <span class="tool-badge"><i class="pi pi-code" /> Any IDE</span>
          </div>
        </div>

        <p class="hero-no-lock"><span class="hero-check">✓</span> No lock-in. Run locally or in the cloud.</p>
      </div>

      <!-- Right: terminal -->
      <div class="hero-terminal">
        <div class="terminal-bar">
          <span class="dot red" />
          <span class="dot yellow" />
          <span class="dot green" />
          <span class="terminal-title">Terminal</span>
        </div>
        <div class="terminal-body">
          <div class="terminal-line">
            <span class="terminal-prompt">$</span>
            <span class="terminal-cmd"> automaton deploy</span>
          </div>
          <div class="terminal-line output">
            <span class="output-check">✓</span> Frontend detected <span class="output-path">/ui</span>
          </div>
          <div class="terminal-line output">
            <span class="output-check">✓</span> Backend detected <span class="output-path">/backend</span>
          </div>
          <div class="terminal-line output">
            <span class="output-check">✓</span> Deploying to Automaton Cloud...
          </div>
          <div class="terminal-line success">
            🚀 Live at <span class="terminal-url">https://automaton.app/app/my-app</span>
          </div>
          <div class="terminal-cursor" />
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.hero {
  position: relative;
  padding: 10px 0 15px;
  overflow: hidden;
  background-image:
    linear-gradient(rgba(124, 106, 245, 0.05) 1px, transparent 1px),
    linear-gradient(90deg, rgba(124, 106, 245, 0.05) 1px, transparent 1px);
  background-size: 48px 48px;
}

.hero-glow {
  position: absolute;
  top: -100px;
  right: -100px;
  width: 700px;
  height: 600px;
  background: radial-gradient(ellipse, rgba(124, 106, 245, 0.22) 0%, transparent 65%);
  pointer-events: none;
}

.hero-inner {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 56px;
  align-items: center;
}

.hero-text {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  text-align: left;
}


.hero-title {
  font-size: clamp(2.2rem, 4.5vw, 3.4rem);
  font-weight: 800;
  line-height: 1.1;
  letter-spacing: -0.03em;
  color: var(--color-text);
  max-width: 700px;
}

.hero-title-line {
  white-space: nowrap;
}

.hero-accent {
  background: linear-gradient(135deg, #7c6af5, #4da6ff);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.hero-subtitle {
  font-size: clamp(1rem, 2vw, 1.15rem);
  color: var(--color-text-muted);
  max-width: 560px;
  margin-top: 16px;
  line-height: 1.7;
}

.hero-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 28px;
  flex-wrap: wrap;
}

.btn-lg {
  padding: 10px 18px;
  font-size: 0.85rem;
}

.hero-no-lock {
  margin-top: 16px;
  font-size: 0.82rem;
  color: var(--color-text-muted);
  font-weight: 500;
}

.hero-check {
  color: var(--color-green);
}

.hero-tools {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 28px;
  flex-wrap: wrap;
  justify-content: center;
}

.tools-label {
  font-size: 0.8rem;
  color: var(--color-text-muted);
  font-weight: 500;
}

.tools-list {
  display: flex;
  align-items: center;
  gap: 18px;
  flex-wrap: wrap;
}

.tool-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.78rem;
  font-weight: 500;
  color: var(--color-text-muted);
}

.tool-badge i {
  font-size: 0.88rem;
  color: rgba(233, 236, 245, 0.72);
}

@media (max-width: 900px) {
  .hero-inner {
    grid-template-columns: 1fr;
    gap: 20px;
  }

  .hero-text {
    align-items: center;
    text-align: center;
  }

  .hero-actions {
    justify-content: center;
  }

  .hero-tools {
    justify-content: center;
  }
}

/* Terminal */
.hero-terminal {
  width: 100%;
  border-radius: var(--radius);
  background: #0d0d14;
  border: 1px solid var(--color-border);
  box-shadow: 0 32px 80px rgba(0, 0, 0, 0.5), 0 0 0 1px rgba(124, 106, 245, 0.1);
  overflow: hidden;
  font-family: var(--font-mono);
  text-align: left;
}

.terminal-bar {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 12px 16px;
  background: #161620;
  border-bottom: 1px solid var(--color-border);
}

.dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
}

.dot.red { background: #ff5f57; }
.dot.yellow { background: #febc2e; }
.dot.green { background: #28c840; }

.terminal-title {
  margin-left: 8px;
  font-size: 0.78rem;
  color: var(--color-text-muted);
  font-family: inherit;
}

.terminal-body {
  padding: 20px 24px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.terminal-line {
  font-size: 0.88rem;
  color: var(--color-text-muted);
  display: flex;
  align-items: center;
  gap: 6px;
}

.terminal-prompt {
  color: var(--color-accent);
  font-weight: 700;
}

.terminal-cmd {
  color: var(--color-text);
}

.output .output-check {
  color: var(--color-green);
}

.output-path {
  color: var(--color-blue);
}

.terminal-line.success {
  color: var(--color-green);
  margin-top: 4px;
  font-weight: 600;
}

.terminal-url {
  color: var(--color-accent);
  text-decoration: underline;
  cursor: pointer;
}

.terminal-cursor {
  display: inline-block;
  width: 8px;
  height: 16px;
  background: var(--color-accent);
  margin-top: 4px;
  border-radius: 2px;
  animation: blink 1s infinite;
}

@keyframes blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0; }
}
</style>
