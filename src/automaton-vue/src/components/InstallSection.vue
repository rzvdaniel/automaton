<script setup lang="ts">
import installRocketImage from '@/assets/images/install-rocket.png'
</script>

<template>
  <section id="install" class="install-section">
    <div class="container">
      <div class="install-card">
        <img class="install-art" :src="installRocketImage" alt="" aria-hidden="true" />

        <div class="install-command-wrap">
          <div class="install-command">
            <span class="install-prompt">$</span>
            <span class="install-cmd">npm install -g @automaton/cli</span>
            <button class="copy-btn" title="Copy">
              <i class="pi pi-copy" />
            </button>
          </div>
        </div>

        <div class="install-content">
          <h2 class="install-title">Ship your first app in minutes</h2>
          <p class="install-subtitle">Install the CLI and deploy instantly.</p>

          <div class="install-actions">
            <a href="#install" class="btn btn-primary btn-lg">
              <i class="pi pi-download" />
              Install CLI
            </a>
            <a href="/docs" class="btn btn-secondary btn-lg">
              <i class="pi pi-book" />
              Read the docs
            </a>
          </div>
        </div>

        <div class="install-rocket" aria-hidden="true">
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.install-section {
  padding: 10px 0 30px;
}

.install-card {
  position: relative;
  display: grid;
  grid-template-columns: 330px minmax(0, 1fr) 310px;
  gap: 34px;
  align-items: center;
  min-height: 190px;
  padding: 15px 10px;
  border-radius: 8px;
  overflow: hidden;
  border: 1px solid rgba(151, 111, 255, 0.32);
  background: #25124b;
}

.install-art {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: contain;
  object-position: right center;
  opacity: 1;
  pointer-events: none;
}

.install-command-wrap,
.install-content,
.install-rocket {
  position: relative;
  z-index: 1;
}

.install-command-top {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 8px;
  color: rgba(255, 255, 255, 0.55);
  font-size: 0.9rem;
}

.install-command {
  display: flex;
  align-items: center;
  gap: 12px;
  min-height: 76px;
  padding: 0 22px;
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  background: rgba(9, 11, 18, 0.88);
  font-family: var(--font-mono);
  font-size: 0.9rem;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.03);
}

.install-prompt {
  color: #ffffff;
  font-weight: 700;
}

.install-cmd {
  flex: 1;
  min-width: 0;
  color: #8ef0a1;
}

.copy-btn {
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.68);
  cursor: pointer;
  padding: 2px 4px;
  transition: color 0.2s;
  font-size: 0.9rem;
}

.copy-btn:hover {
  color: var(--color-text);
}

.install-title {
  font-size: 1.5rem;
  font-weight: 700;
  letter-spacing: 0;
  color: var(--color-text);
  margin-bottom: 8px;
  line-height: 1.2;
}

.install-subtitle {
  font-size: 1rem;
  color: rgba(240, 240, 245, 0.78);
  margin-bottom: 20px;
}

.install-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.install-rocket {
  position: relative;
  width: 100%;
  min-height: 110px;
}

.btn-lg {
  padding: 14px 24px;
  font-size: 0.95rem;
}

@media (max-width: 900px) {
  .install-card {
    grid-template-columns: 1fr;
    gap: 20px;
    padding: 24px 18px;
  }

  .install-art {
    width: 100%;
    opacity: 0.34;
  }

  .install-rocket {
    display: none;
  }
}
</style>
