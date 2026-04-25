<template>
  <section id="install" class="install-section">
    <div class="container">
      <div class="install-card">
        <div class="install-glow" aria-hidden="true" />

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
          <div class="rocket-outline"></div>
          <div class="rocket-trail trail-one"></div>
          <div class="rocket-trail trail-two"></div>
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
  grid-template-columns: 290px minmax(0, 1fr) 210px;
  gap: 28px;
  align-items: center;
  padding: 34px 24px;
  border-radius: 18px;
  overflow: hidden;
  border: 1px solid rgba(151, 111, 255, 0.32);
  background:
    radial-gradient(circle at 85% 50%, rgba(159, 104, 255, 0.2), transparent 30%),
    linear-gradient(180deg, rgba(43, 21, 82, 0.98), rgba(29, 16, 54, 0.98));
}

.install-glow {
  position: absolute;
  right: -80px;
  bottom: -120px;
  width: 360px;
  height: 260px;
  background: radial-gradient(ellipse, rgba(182, 112, 255, 0.22) 0%, transparent 66%);
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
  min-height: 74px;
  padding: 0 20px;
  border-radius: 14px;
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
  font-size: clamp(1.9rem, 3.2vw, 2.55rem);
  font-weight: 700;
  letter-spacing: -0.03em;
  color: var(--color-text);
  margin-bottom: 8px;
}

.install-subtitle {
  font-size: 1rem;
  color: rgba(240, 240, 245, 0.78);
  margin-bottom: 22px;
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
  min-height: 160px;
}

.rocket-outline {
  position: absolute;
  right: 58px;
  top: 8px;
  width: 74px;
  height: 132px;
  border: 3px solid rgba(225, 202, 255, 0.92);
  border-radius: 44px 44px 18px 18px;
  transform: rotate(22deg);
  filter: drop-shadow(0 0 18px rgba(190, 125, 255, 0.45));
}

.rocket-outline::before,
.rocket-outline::after {
  content: '';
  position: absolute;
}

.rocket-outline::before {
  width: 24px;
  height: 36px;
  left: -18px;
  bottom: 28px;
  border: 3px solid rgba(225, 202, 255, 0.92);
  border-top: none;
  border-radius: 0 0 18px 18px;
  transform: skewY(28deg);
}

.rocket-outline::after {
  width: 20px;
  height: 20px;
  top: 28px;
  left: 24px;
  border: 3px solid rgba(225, 202, 255, 0.92);
  border-radius: 50%;
  box-shadow: 0 0 16px rgba(199, 144, 255, 0.45);
}

.rocket-trail {
  position: absolute;
  height: 10px;
  border-radius: 999px;
  background: linear-gradient(90deg, rgba(182, 112, 255, 0), rgba(182, 112, 255, 0.7));
  transform: rotate(22deg);
  filter: blur(1px);
}

.trail-one {
  width: 118px;
  right: 16px;
  bottom: 48px;
}

.trail-two {
  width: 74px;
  right: 60px;
  bottom: 28px;
  opacity: 0.55;
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

  .install-rocket {
    min-height: 120px;
  }

  .rocket-outline {
    right: 18px;
    top: -4px;
  }
}
</style>
