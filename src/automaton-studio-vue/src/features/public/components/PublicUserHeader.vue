<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import Avatar from 'primevue/avatar'
import Button from 'primevue/button'
import type { PublicUserProfile } from '../types/publicApps.types'
import { getInitials } from '../types/publicApps.types'

const router = useRouter()
const authStore = useAuthStore()

const { profile } = defineProps<{
  profile: PublicUserProfile
}>()

// Check if authenticated user is viewing their own profile
const isOwnProfile = () => {
  return authStore.isAuthenticated && authStore.publicUsername === profile.username
}

// Navigate to dashboard
const goToDashboard = () => {
  router.push('/apps')
}

// Navigate to settings
const goToSettings = () => {
  router.push('/settings')
}
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
        <div class="user-info-top">
          <div>
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
          <!-- Action buttons for authenticated users viewing their own profile -->
          <div v-if="isOwnProfile()" class="profile-actions">
            <Button
              size="small"
              outlined
              icon="pi pi-pencil"
              title="Edit profile settings"
              @click="goToSettings"
            />
            <Button
              size="small"
              icon="pi pi-home"
              title="Go to dashboard"
              @click="goToDashboard"
            />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.public-user-header {
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid rgba(103, 193, 245, 0.1);
  background: rgba(14, 20, 27, 0.34);
}

:global(.app-dark) .public-user-header {
  border-color: var(--p-surface-700);
}

.header-content {
  max-width: 1280px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  gap: 1rem;
}

.user-avatar {
  flex-shrink: 0;
  width: 72px !important;
  height: 72px !important;
  font-size: 1.5rem;
}

.avatar-fallback {
  background: linear-gradient(135deg, #66c0f4 0%, #1a9fff 100%);
  color: #0b1118;
}

.avatar-initials {
  font-size: 1.35rem;
  font-weight: 600;
}

.user-info {
  flex: 1;
  min-width: 0;
}

.user-info-top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.profile-actions {
  display: flex;
  gap: 0.5rem;
  flex-shrink: 0;
}

.user-name {
  margin: 0 0 0.25rem 0;
  font-size: 1.35rem;
  font-weight: 700;
  color: #ffffff;
}

.user-username {
  margin: 0 0 0.5rem 0;
  color: #66c0f4;
  font-size: 1rem;
}

.user-bio {
  margin: 0 0 0.75rem 0;
  color: #acb9c7;
  line-height: 1.5;
  max-width: 720px;
}

.user-stats {
  display: flex;
  gap: 1.5rem;
}

.stat {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #8f98a0;
  font-size: 0.95rem;
}

.stat i {
  color: #66c0f4;
}

.stat strong {
  color: #ffffff;
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
