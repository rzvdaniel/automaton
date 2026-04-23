<script setup lang="ts">
// Install / CTA section
</script>

<template>
  <section id="install" class="install-section">
    <div class="container">
      <div class="install-card">
        <div class="install-glow" aria-hidden="true" />

        <!-- Left: content -->
        <div class="install-content">
          <h2 class="install-title">Ship your first app in minutes</h2>
          <p class="install-subtitle">Install the CLI and deploy instantly.</p>

          <div class="install-command">
            <span class="install-prompt">$</span>
            <span class="install-cmd">npm install -g @automaton/cli</span>
            <button class="copy-btn" title="Copy">
              <i class="pi pi-copy" />
            </button>
          </div>

          <div class="install-actions">
            <a href="#install" class="btn btn-primary btn-lg">
              <i class="pi pi-download" />
              Install CLI
            </a>
            <a href="/docs" class="btn btn-secondary btn-lg">
              <i class="pi pi-book" />
              Read docs
            </a>
          </div>
          <p class="install-note">No account required to start.</p>
        </div>

        <!-- Right: rocket illustration -->
        <div class="install-rocket" aria-hidden="true">
          <div class="rocket-glow" />
          <div class="rocket-emoji">🚀</div>
          <div class="rocket-trail">
            <span /><span /><span />
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
  padding: 64px 56px;
  overflow: hidden;
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 32px;
  align-items: center;
}

.install-glow {
  position: absolute;
  top: -80px;
  left: -80px;
  width: 500px;
  height: 400px;
  background: radial-gradient(ellipse, rgba(124, 106, 245, 0.18) 0%, transparent 65%);
  pointer-events: none;
}

.install-content {
  position: relative;
  z-index: 1;
  text-align: left;
}

.install-title {
  font-size: clamp(1.6rem, 3.5vw, 2.4rem);
  font-weight: 800;
  letter-spacing: -0.03em;
  color: var(--color-text);
  margin-bottom: 10px;
}

.install-subtitle {
  font-size: 1rem;
  color: var(--color-text-muted);
  margin-bottom: 28px;
}

.install-command {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  padding: 12px 18px;
  border-radius: var(--radius);
  background: #0d0d14;
  border: 1px solid var(--color-border);
  font-family: var(--font-mono);
  font-size: 0.9rem;
  margin-bottom: 28px;
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
  gap: 12px;
  flex-wrap: wrap;
}

.install-note {
  margin-top: 16px;
  font-size: 0.8rem;
  color: var(--color-text-muted);
}

/* Rocket illustration */
.install-rocket {
  position: relative;
  width: 180px;
  height: 180px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.rocket-glow {
  position: absolute;
  inset: 0;
  background: radial-gradient(ellipse, rgba(124, 106, 245, 0.3) 0%, transparent 70%);
  border-radius: 50%;
}

.rocket-emoji {
  font-size: 5rem;
  transform: rotate(-45deg);
  position: relative;
  z-index: 1;
  filter: drop-shadow(0 0 20px rgba(124, 106, 245, 0.5));
  animation: float 3s ease-in-out infinite;
}

@keyframes float {
  0%, 100% { transform: rotate(-45deg) translateY(0); }
  50% { transform: rotate(-45deg) translateY(-8px); }
}

.rocket-trail {
  position: absolute;
  bottom: 30px;
  right: 30px;
  display: flex;
  flex-direction: column;
  gap: 5px;
  transform: rotate(-45deg);
}

.rocket-trail span {
  display: block;
  height: 3px;
  border-radius: 2px;
  background: linear-gradient(90deg, transparent, rgba(124, 106, 245, 0.6));
}

.rocket-trail span:nth-child(1) { width: 40px; }
.rocket-trail span:nth-child(2) { width: 28px; opacity: 0.6; }
.rocket-trail span:nth-child(3) { width: 18px; opacity: 0.3; }

@media (max-width: 768px) {
  .install-card {
    grid-template-columns: 1fr;
    padding: 40px 28px;
  }

  .install-rocket {
    display: none;
  }
}

.btn-lg {
  padding: 14px 28px;
  font-size: 0.95rem;
}
</style>
