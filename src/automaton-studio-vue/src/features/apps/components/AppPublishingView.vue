<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import type { App } from '../types/apps.types'
import { getPublishedAppUrl } from '../utils/public-app-url'

const props = defineProps<{
  app: App
  hasLiveTarget: boolean
}>()

const emit = defineEmits<{
  publish: []
  unpublish: []
}>()
const toast = useToast()

const publishedUrl = computed(() =>
  props.app.isPublished && props.app.publicUrl
    ? getPublishedAppUrl(props.app.publicUrl)
    : null,
)

const runtimeVisibility = computed(() => {
  if (props.app.runtimeVisibility === 'public') return 'Public runtime'
  if (props.app.runtimeVisibility === 'private') return 'Private runtime'
  return 'Not configured'
})

const marketplaceVisibility = computed(() => {
  if (props.app.catalogVisibility === 'listed') return 'Listed in marketplace'
  if (props.app.catalogVisibility === 'unlisted') return 'Unlisted'
  return 'Not configured'
})

const accessDescription = computed(() => {
  if (props.app.requiresAuthentication) return 'Sign-in required'
  if (props.app.runtimeVisibility === 'private') return 'Private runtime access'
  if (props.app.canLaunchPublicly === true) return 'Anyone with the published URL can open this app'
  if (props.app.canLaunchPublicly === false) return 'Public launch is unavailable'
  return 'Access details are not available'
})

function formatLastViewed(date: string | null | undefined): string {
  if (!date) return 'Never'
  const viewedAt = new Date(date)
  if (Number.isNaN(viewedAt.getTime())) return 'Date unavailable'
  const elapsedMs = Date.now() - viewedAt.getTime()
  const elapsedMinutes = Math.floor(elapsedMs / 60_000)
  const elapsedHours = Math.floor(elapsedMs / 3_600_000)
  const elapsedDays = Math.floor(elapsedMs / 86_400_000)

  if (elapsedMinutes < 1) return 'Just now'
  if (elapsedMinutes < 60) return `${elapsedMinutes}m ago`
  if (elapsedHours < 24) return `${elapsedHours}h ago`
  if (elapsedDays < 7) return `${elapsedDays}d ago`
  return viewedAt.toLocaleDateString()
}

async function copyPublishedUrl() {
  if (!publishedUrl.value) return
  try {
    await navigator.clipboard.writeText(publishedUrl.value)
    toast.add({ severity: 'success', summary: 'Copied', detail: 'Public URL copied to clipboard', life: 2000 })
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to copy URL', life: 3000 })
  }
}
</script>

<template>
  <section class="publishing-view" aria-labelledby="publishing-title">
    <header class="publishing-header">
      <div>
        <p class="eyebrow">App management</p>
        <h1 id="publishing-title">Publishing</h1>
        <p class="muted">Manage how this app is shared. Publishing is separate from deploying the Live runtime.</p>
      </div>
      <Tag
        :value="app.isPublished ? 'Published' : 'Unpublished'"
        :severity="app.isPublished ? 'success' : 'secondary'"
      />
    </header>

    <div class="publishing-grid">
      <Card class="publishing-card publishing-card--primary">
        <template #title>Published app</template>
        <template #content>
          <p v-if="app.isPublished" class="state-copy">
            This app is published. Changes to the deployed runtime still require a new deployment.
          </p>
          <p v-else class="state-copy">
            This app is not published. You can deploy and operate it in Studio without making it public.
          </p>

          <div v-if="publishedUrl" class="published-url">
            <span class="field-label">Public URL</span>
            <div class="published-url__actions">
              <a :href="publishedUrl" target="_blank" rel="noopener noreferrer">
                {{ publishedUrl }}
                <i class="pi pi-external-link" aria-hidden="true"></i>
              </a>
              <Button label="Copy URL" icon="pi pi-copy" text size="small" @click="copyPublishedUrl" />
            </div>
          </div>
          <p v-else class="muted url-placeholder">
            {{ app.isPublished ? 'No public URL is configured.' : 'A public URL will be assigned when you publish this app.' }}
          </p>

          <dl v-if="app.isPublished" class="publishing-analytics">
            <div>
              <dt>Total views</dt>
              <dd>{{ app.viewCount }}</dd>
            </div>
            <div>
              <dt>Last viewed</dt>
              <dd>{{ formatLastViewed(app.lastViewedAt) }}</dd>
            </div>
          </dl>

          <div class="publishing-actions">
            <Button
              v-if="app.isPublished"
              label="Update publishing settings"
              icon="pi pi-cog"
              @click="emit('publish')"
            />
            <Button
              v-else
              label="Publish Live"
              icon="pi pi-globe"
              :disabled="!hasLiveTarget"
              @click="emit('publish')"
            />
            <Button
              v-if="app.isPublished"
              label="Unpublish"
              icon="pi pi-eye-slash"
              severity="danger"
              outlined
              @click="emit('unpublish')"
            />
          </div>
          <p v-if="!hasLiveTarget && !app.isPublished" class="muted action-note">
            Create a Live deployment target before publishing.
          </p>
        </template>
      </Card>

      <Card class="publishing-card">
        <template #title>Visibility and access</template>
        <template #content>
          <dl class="visibility-list">
            <div>
              <dt>Runtime</dt>
              <dd>{{ runtimeVisibility }}</dd>
            </div>
            <div>
              <dt>Marketplace</dt>
              <dd>{{ marketplaceVisibility }}</dd>
            </div>
            <div>
              <dt>App access</dt>
              <dd>{{ accessDescription }}</dd>
            </div>
            <div>
              <dt>Remixing</dt>
              <dd>{{ app.isForkable ? 'Enabled' : 'Disabled' }}</dd>
            </div>
          </dl>
          <p class="muted access-note">
            Publishing controls update these options using the existing Publish Live workflow.
          </p>
        </template>
      </Card>
    </div>
  </section>
</template>

<style scoped>
.publishing-view {
  height: 100%;
  overflow-y: auto;
  padding: 1.5rem;
}

.publishing-header,
.publishing-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.publishing-header {
  margin-bottom: 1.25rem;
}

.publishing-header h1 {
  margin: 0 0 0.4rem;
}

.publishing-header p,
.state-copy,
.url-placeholder,
.access-note {
  margin: 0;
}

.eyebrow,
.field-label {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.eyebrow {
  margin-bottom: 0.35rem !important;
}

.muted {
  color: var(--p-text-muted-color);
}

.publishing-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(0, 0.8fr);
  gap: 1rem;
}

.publishing-card {
  border: 1px solid var(--p-surface-border);
  border-radius: 0.9rem;
}

.publishing-card--primary {
  border-top: 3px solid var(--p-primary-color);
}

.publishing-card :deep(.p-card-content) {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.published-url {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 0.4rem;
}

.published-url a {
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
  overflow-wrap: anywhere;
  color: var(--p-primary-color);
  font-weight: 600;
  text-decoration: none;
}

.published-url__actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.publishing-actions {
  justify-content: flex-start;
  flex-wrap: wrap;
}

.publishing-analytics {
  display: flex;
  flex-wrap: wrap;
  gap: 1.5rem;
  margin: 0;
  padding-top: 0.85rem;
  border-top: 1px solid var(--p-surface-border);
}

.publishing-analytics > div {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.publishing-analytics dt {
  color: var(--p-text-muted-color);
  font-size: 0.85rem;
}

.publishing-analytics dd {
  margin: 0;
  font-weight: 600;
}

.action-note {
  font-size: 0.85rem;
}

.visibility-list {
  display: grid;
  gap: 0.9rem;
  margin: 0;
}

.visibility-list > div {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.visibility-list dt {
  color: var(--p-text-muted-color);
}

.visibility-list dd {
  margin: 0;
  text-align: right;
  font-weight: 600;
}

.access-note {
  font-size: 0.85rem;
}

@media (max-width: 900px) {
  .publishing-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 600px) {
  .publishing-header {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
