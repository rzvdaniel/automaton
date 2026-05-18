<script setup lang="ts">
import AppFooter from '@/components/AppFooter.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
</script>

<template>
  <div class="pricing-page">
    <PublicSiteHeader />

    <main class="pricing-main">
      <section class="hero">
        <div class="shell hero-inner">
          <span class="eyebrow">Current Access</span>
          <h1>Developer-first access, with AI and runtime costs under your control.</h1>
          <p>
            Automaton Monster currently uses a bring-your-own-model approach so developers can work
            with their preferred AI providers, publish static apps freely, and run backend apps on
            their own Runner containers.
          </p>
        </div>
      </section>

      <section class="plans">
        <div class="shell plan-grid">
          <article class="plan-card plan-card-primary">
            <div class="plan-top">
              <div>
                <span class="plan-badge">Available now</span>
                <h2>Free</h2>
              </div>
              <div class="plan-price">$0</div>
            </div>

            <p class="plan-copy">
              Static apps can be built, published, forked, and shared at no cost.
            </p>

            <ul class="feature-list">
              <li>Build and publish static apps</li>
              <li>Fork public apps</li>
              <li>Public app hosting</li>
              <li>CLI + Studio access</li>
              <li>Bring your own AI provider</li>
              <li>Use your own Runner containers for backend apps</li>
            </ul>

            <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener" class="button primary">
              Start building
            </a>
          </article>

          <article class="plan-card">
            <div class="plan-top">
              <div>
                <span class="plan-badge plan-badge-soon">Coming soon</span>
                <h2>Managed Runners</h2>
              </div>
              <div class="plan-price muted">Later</div>
            </div>

            <p class="plan-copy">
              Hosted backend execution for teams that want simpler deployment and less runner
              infrastructure to manage themselves.
            </p>

            <ul class="feature-list">
              <li>Hosted backend execution</li>
              <li>Managed Runner provisioning</li>
              <li>Persistent backend apps</li>
              <li>Simplified deployment</li>
              <li>Scalable runtime options</li>
            </ul>

            <a href="mailto:hello@automaton.app?subject=Managed%20Runners%20waitlist" class="button">
              Join waitlist
            </a>
          </article>
        </div>
      </section>

      <section class="explainers">
        <div class="shell explainer-grid">
          <article class="info-card">
            <h2>Why BYOK?</h2>
            <p>
              Bring-your-own-model is a product choice, not a temporary compromise. It keeps AI
              costs visible, lets developers use their preferred models, and avoids bundling hidden
              AI subscriptions into the platform.
            </p>
            <ul class="feature-list compact">
              <li>Keep control over AI usage and cost</li>
              <li>Use existing AI tools and providers</li>
              <li>No opaque token bundles</li>
              <li>Fits existing agentic coding workflows</li>
            </ul>
          </article>

          <article class="info-card">
            <h2>Backend apps today</h2>
            <p>
              Frontend-only apps are the simplest path and can be published immediately. Backend
              apps work today as well, using self-hosted Runners for execution.
            </p>
            <ul class="feature-list compact">
              <li>Frontend apps: free to build and publish</li>
              <li>Backend apps: deploy through your own Runner</li>
              <li>Good fit for developers who want runtime control</li>
            </ul>
          </article>
        </div>
      </section>
    </main>

    <AppFooter />
  </div>
</template>

<style scoped>
.pricing-page {
  min-height: 100vh;
  background:
    radial-gradient(circle at 24% 0%, rgba(80, 215, 255, 0.12), transparent 30rem),
    radial-gradient(circle at 86% 10%, rgba(69, 230, 138, 0.1), transparent 28rem),
    linear-gradient(180deg, #090b10 0%, #07080b 44%, #050608 100%);
  color: #f5f7fb;
}

.shell {
  width: min(1260px, calc(100% - 40px));
  margin: 0 auto;
}

.pricing-main {
  padding: 34px 0 84px;
}

.hero {
  padding: 24px 0 18px;
}

.hero-inner {
  max-width: 900px;
}

.eyebrow {
  display: inline-flex;
  padding: 5px 10px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.78rem;
  font-weight: 850;
  text-transform: uppercase;
}

.hero h1 {
  margin: 16px 0 12px;
  font-size: clamp(2.3rem, 5vw, 4.8rem);
  line-height: 0.92;
  letter-spacing: -0.06em;
}

.hero p {
  max-width: 760px;
  margin: 0;
  color: #c8ceda;
  font-size: 1.06rem;
  line-height: 1.65;
}

.plans,
.explainers {
  padding-top: 22px;
}

.plan-grid,
.explainer-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.plan-card,
.info-card {
  padding: 22px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.26);
}

.plan-card-primary {
  background:
    linear-gradient(135deg, rgba(69, 230, 138, 0.08), rgba(80, 215, 255, 0.04)),
    rgba(255, 255, 255, 0.04);
}

.plan-top {
  display: flex;
  align-items: start;
  justify-content: space-between;
  gap: 18px;
}

.plan-badge {
  display: inline-flex;
  margin-bottom: 10px;
  padding: 4px 9px;
  border-radius: 999px;
  background: rgba(69, 230, 138, 0.14);
  color: #45e68a;
  font-size: 0.74rem;
  font-weight: 800;
  text-transform: uppercase;
}

.plan-badge-soon {
  background: rgba(255, 203, 87, 0.12);
  color: #ffcb57;
}

.plan-top h2,
.info-card h2 {
  margin: 0;
  font-size: 1.4rem;
  letter-spacing: -0.03em;
}

.plan-price {
  font-size: 2rem;
  font-weight: 800;
  letter-spacing: -0.05em;
}

.plan-price.muted {
  color: #9ba3b4;
}

.plan-copy,
.info-card p {
  margin: 14px 0 0;
  color: #c8ceda;
  line-height: 1.65;
}

.feature-list {
  margin: 18px 0 22px;
  padding-left: 1.2rem;
  color: #f5f7fb;
}

.feature-list li + li {
  margin-top: 0.6rem;
}

.feature-list.compact {
  margin-bottom: 0;
}

.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 40px;
  padding: 0 16px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.03);
  color: #f5f7fb;
  font-size: 0.92rem;
  font-weight: 700;
  text-decoration: none;
}

.button.primary {
  border-color: rgba(69, 230, 138, 0.58);
  background: #45e68a;
  color: #041008;
  box-shadow: 0 16px 44px rgba(69, 230, 138, 0.18);
}

@media (max-width: 960px) {
  .plan-grid,
  .explainer-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .shell {
    width: min(100% - 28px, 1180px);
  }

  .pricing-main {
    padding-top: 24px;
  }
}
</style>
