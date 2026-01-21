<script setup lang="ts">
import Avatar from 'primevue/avatar'
import type { PublicUserProfile } from '../types/publicApps.types'
import { getInitials } from '../types/publicApps.types'

defineProps<{
  profile: PublicUserProfile
}>()
</script>

<template>
  <div class="public-user-header">
    <div class="header-content">
      <!-- Avatar -->
      <Avatar
        v-if="profile.avatarUrl"
        :image="profile.avatarUrl"
        size="xlarge"
        shape="circle"
        class="user-avatar"
      />
      <Avatar v-else size="xlarge" shape="circle" class="user-avatar avatar-fallback">
        <template #default>
          <span class="avatar-initials">{{ getInitials(profile.displayName || profile.username) }}</span>
        </template>
      </Avatar>

      <!-- User Info -->
      <div class="user-info">
        <h1 class="user-name">{{ profile.displayName || profile.username }}</h1>
        <p v-if="profile.displayName" class="user-username">@{{ profile.username }}</p>
        <p v-if="profile.bio" class="user-bio">{{ profile.bio }}</p>
        <div class="user-stats">
          <span class="stat">
            <i class="pi pi-box"></i>
            <strong>{{ profile.publicAppCount }}</strong> public {{ profile.publicAppCount === 1 ? 'app' : 'apps' }}
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.public-user-header {
  padding: 2rem;
  margin-bottom: 2rem;
  border-bottom: 1px solid var(--p-surface-200);
}

:global(.app-dark) .public-user-header {
  border-color: var(--p-surface-700);
}

.header-content {
  max-width: 1200px;
  margin: 0 auto;
  display: flex;
  align-items: flex-start;
  gap: 1.5rem;
}

.user-avatar {
  flex-shrink: 0;
  width: 100px !important;
  height: 100px !important;
  font-size: 2rem;
}

.avatar-fallback {
  background: linear-gradient(135deg, var(--p-primary-400) 0%, var(--p-primary-600) 100%);
  color: white;
}

.avatar-initials {
  font-size: 2rem;
  font-weight: 600;
}

.user-info {
  flex: 1;
  min-width: 0;
}

.user-name {
  margin: 0 0 0.25rem 0;
  font-size: 2rem;
  font-weight: 700;
  color: var(--p-text-color);
}

.user-username {
  margin: 0 0 0.5rem 0;
  color: var(--p-text-muted-color);
  font-size: 1rem;
}

.user-bio {
  margin: 0 0 1rem 0;
  color: var(--p-text-color);
  line-height: 1.5;
  max-width: 600px;
}

.user-stats {
  display: flex;
  gap: 1.5rem;
}

.stat {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: var(--p-text-muted-color);
  font-size: 0.95rem;
}

.stat i {
  color: var(--p-primary-500);
}

.stat strong {
  color: var(--p-text-color);
}

@media (max-width: 600px) {
  .header-content {
    flex-direction: column;
    align-items: center;
    text-align: center;
  }

  .user-bio {
    max-width: 100%;
  }

  .user-stats {
    justify-content: center;
  }
}
</style>
