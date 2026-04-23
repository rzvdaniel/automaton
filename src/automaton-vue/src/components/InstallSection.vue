<script setup lang="ts">
// Install / CTA section
</script>

<template>
  <section id="install" class="install-section">
    <div class="container">
      <div class="install-card">
        <div class="install-glow" aria-hidden="true" />
        <div class="install-content">
          <h2 class="install-title">Ready to ship?</h2>
          <p class="install-subtitle">
            Install the CLI in seconds and deploy your first app today.
          </p>

          <div class="install-command">
            <span class="install-prompt">$</span>
            <span class="install-cmd">npm install -g @automaton/cli</span>
            <button class="copy-btn" title="Copy">
              <i class="pi pi-copy" />
            </button>
          </div>

          <div class="install-actions">
            <a href="/docs" class="btn btn-primary btn-lg">
              Read the docs
              <i class="pi pi-book" />
            </a>
            <a href="https://github.com" target="_blank" class="btn btn-secondary btn-lg">
              <i class="pi pi-github" />
              View on GitHub
            </a>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.install-section {
  padding: 100px 0;
}

.install-card {
  position: relative;
  border-radius: 20px;
  background: var(--color-surface);
  border: 1px solid rgba(124, 106, 245, 0.2);
  padding: 72px 48px;
  text-align: center;
  overflow: hidden;
}

.install-glow {
  position: absolute;
  top: -100px;
  left: 50%;
  transform: translateX(-50%);
  width: 600px;
  height: 400px;
  background: radial-gradient(ellipse, rgba(124, 106, 245, 0.15) 0%, transparent 70%);
  pointer-events: none;
}

.install-content {
  position: relative;
  z-index: 1;
}

.install-title {
  font-size: clamp(2rem, 4vw, 3rem);
  font-weight: 800;
  letter-spacing: -0.03em;
  color: var(--color-text);
  margin-bottom: 12px;
}

.install-subtitle {
  font-size: 1.05rem;
  color: var(--color-text-muted);
  margin-bottom: 36px;
}

.install-command {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  padding: 14px 20px;
  border-radius: var(--radius);
  background: #0d0d14;
  border: 1px solid var(--color-border);
  font-family: var(--font-mono);
  font-size: 0.95rem;
  margin-bottom: 36px;
}

.install-prompt {
  color: var(--color-accent);
  font-weight: 700;
}

.install-cmd {
  color: var(--color-text);
}

.copy-btn {
  background: none;
  border: none;
  color: var(--color-text-muted);
  cursor: pointer;
  padding: 2px 4px;
  transition: color 0.2s;
  font-size: 0.9rem;
}

.copy-btn:hover {
  color: var(--color-text);
}

.install-actions {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  flex-wrap: wrap;
}

.btn-lg {
  padding: 14px 28px;
  font-size: 0.95rem;
}
</style>
