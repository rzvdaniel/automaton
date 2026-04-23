<script setup lang="ts">
// Footer
</script>

<template>
  <footer class="site-footer">
    <div class="container footer-inner">
      <div class="footer-brand">
        <div class="footer-logo">
          <span>⚡</span>
          <span>Automaton</span>
        </div>
        <p class="footer-tagline">
          The fastest way to turn your code into a live app.
        </p>
        <div class="footer-social">
          <a href="https://twitter.com" target="_blank" class="social-link" aria-label="Twitter">
            <i class="pi pi-twitter" />
          </a>
          <a href="https://github.com" target="_blank" class="social-link" aria-label="GitHub">
            <i class="pi pi-github" />
          </a>
        </div>
      </div>

      <div class="footer-links">
        <div class="footer-col">
          <h4 class="footer-col-title">Product</h4>
          <a href="/apps" class="footer-link">Apps</a>
          <a href="/docs" class="footer-link">Docs</a>
          <a href="#how-it-works" class="footer-link">How it works</a>
          <a href="#install" class="footer-link">CLI</a>
        </div>
        <div class="footer-col">
          <h4 class="footer-col-title">Resources</h4>
          <a href="/docs" class="footer-link">Getting started</a>
          <a href="/docs#backend" class="footer-link">Backend guide</a>
          <a href="/docs#deploy" class="footer-link">Deploy guide</a>
          <a href="https://github.com" target="_blank" class="footer-link">GitHub</a>
        </div>
        <div class="footer-col">
          <h4 class="footer-col-title">Company</h4>
          <a href="#" class="footer-link">About</a>
          <a href="mailto:hello@automaton.app" class="footer-link">Contact</a>
          <a href="#" class="footer-link">Privacy</a>
          <a href="#" class="footer-link">Terms</a>
        </div>
      </div>
    </div>

    <div class="footer-bottom">
      <div class="container footer-bottom-inner">
        <span>© 2024 Automaton. Built by a solo developer.</span>
        <span class="indie-note">
          Made with ❤️ — open to collaboration. <a href="mailto:hello@automaton.app">Get in touch →</a>
        </span>
      </div>
    </div>
  </footer>
</template>

<style scoped>
.site-footer {
  border-top: 1px solid var(--color-border);
  background: var(--color-surface);
}

.footer-inner {
  display: grid;
  grid-template-columns: 1.4fr 1fr 1fr 1fr;
  gap: 48px;
  padding-top: 64px;
  padding-bottom: 48px;
}

.footer-brand {
  grid-column: 1;
}

.footer-links {
  grid-column: 2 / -1;
  display: contents;
}

.footer-logo {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 700;
  font-size: 1.05rem;
  color: var(--color-text);
  margin-bottom: 12px;
}

.footer-tagline {
  font-size: 0.86rem;
  color: var(--color-text-muted);
  line-height: 1.6;
  max-width: 240px;
  margin-bottom: 20px;
}

.footer-social {
  display: flex;
  gap: 8px;
}

.social-link {
  width: 34px;
  height: 34px;
  border-radius: var(--radius-sm);
  background: var(--color-surface-2);
  border: 1px solid var(--color-border);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--color-text-muted);
  font-size: 0.9rem;
  transition: color 0.2s, border-color 0.2s;
}

.social-link:hover {
  color: var(--color-text);
  border-color: rgba(255, 255, 255, 0.2);
}

.footer-col {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.footer-col-title {
  font-size: 0.8rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-text);
  margin-bottom: 4px;
}

.footer-link {
  font-size: 0.875rem;
  color: var(--color-text-muted);
  text-decoration: none;
  transition: color 0.2s;
}

.footer-link:hover {
  color: var(--color-text);
}

.footer-bottom {
  border-top: 1px solid var(--color-border);
}

.footer-bottom-inner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 20px;
  padding-bottom: 24px;
  font-size: 0.82rem;
  color: var(--color-text-muted);
  flex-wrap: wrap;
  gap: 8px;
}

.indie-note a {
  color: var(--color-accent);
  text-decoration: none;
}

.indie-note a:hover {
  text-decoration: underline;
}

@media (max-width: 900px) {
  .footer-inner {
    grid-template-columns: 1fr 1fr;
    gap: 32px;
  }

  .footer-brand {
    grid-column: 1 / -1;
  }

  .footer-links {
    display: contents;
  }
}

@media (max-width: 600px) {
  .footer-inner {
    grid-template-columns: 1fr;
  }
}
</style>
