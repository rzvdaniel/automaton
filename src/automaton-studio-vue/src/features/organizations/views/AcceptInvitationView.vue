<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { organizationsService } from '../services/organizationsService'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Card from 'primevue/card'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const loading = ref(true)
const error = ref<string | null>(null)
const success = ref(false)

onMounted(async () => {
  const token = route.query.token as string

  if (!token) {
    error.value = 'Invalid invitation link. No token provided.'
    loading.value = false
    return
  }

  try {
    // First, validate and get invitation details without accepting it yet
    // This lets us check if the user needs to set a password
    await organizationsService.acceptInvitation(token)
    success.value = true
    
    toast.add({
      severity: 'success',
      summary: 'Invitation Accepted',
      detail: 'You have successfully joined the organization',
      life: 3000,
    })

    // Redirect to organizations list after a short delay
    setTimeout(() => {
      router.push({ name: 'organizations' })
    }, 2000)
  } catch (err: any) {
    const statusCode = err?.response?.status
    const message = err?.response?.data?.message || 'Failed to accept invitation. The link may be invalid or expired.'
    
    // Check if it's a 401/403 - user needs to log in or set password first
    if (statusCode === 401 || statusCode === 403) {
      // Redirect to login - they can set password after logging in or during registration
      error.value = 'Please log in or create an account to accept this invitation.'
      setTimeout(() => {
        router.push({ name: 'login', query: { redirect: route.fullPath } })
      }, 2000)
    } else {
      error.value = message
    }
    
    toast.add({
      severity: 'error',
      summary: 'Invitation Failed',
      detail: error.value,
      life: 5000,
    })
  } finally {
    loading.value = false
  }
})

function navigateToLogin() {
  router.push({ name: 'login' })
}

function navigateToOrganizations() {
  router.push({ name: 'organizations' })
}
</script>

<template>
  <div class="accept-invitation">
    <Card class="invitation-card">
      <template #title>
        <div class="title-container">
          <i class="pi pi-users" style="font-size: 2rem; margin-bottom: 1rem"></i>
          <h2>Organization Invitation</h2>
        </div>
      </template>

      <template #content>
        <!-- Loading State -->
        <div v-if="loading" class="state-container">
          <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
          <p>Processing invitation...</p>
        </div>

        <!-- Success State -->
        <div v-else-if="success" class="state-container success">
          <i class="pi pi-check-circle" style="font-size: 3rem; color: var(--p-green-500)"></i>
          <h3>Welcome!</h3>
          <p>You have successfully joined the organization.</p>
          <p class="text-muted">Redirecting you to your organizations...</p>
        </div>

        <!-- Error State -->
        <div v-else-if="error" class="state-container error">
          <i class="pi pi-exclamation-triangle" style="font-size: 3rem; color: var(--p-red-500)"></i>
          <h3>Invitation Error</h3>
          <p>{{ error }}</p>
          <div class="button-group">
            <Button 
              label="Go to Login" 
              icon="pi pi-sign-in"
              @click="navigateToLogin"
            />
            <Button 
              label="View Organizations" 
              icon="pi pi-users"
              severity="secondary"
              @click="navigateToOrganizations"
            />
          </div>
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.accept-invitation {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 2rem;
}

.invitation-card {
  max-width: 600px;
  width: 100%;
}

.title-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
}

.title-container h2 {
  margin: 0;
  font-size: 1.5rem;
}

.state-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 2rem 0;
  gap: 1rem;
}

.state-container h3 {
  margin: 0;
  font-size: 1.25rem;
}

.state-container p {
  margin: 0;
  line-height: 1.6;
}

.text-muted {
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}

.button-group {
  display: flex;
  gap: 1rem;
  margin-top: 1rem;
  flex-wrap: wrap;
  justify-content: center;
}

.success i {
  animation: scaleIn 0.3s ease-out;
}

.error i {
  animation: shake 0.5s ease-out;
}

@keyframes scaleIn {
  from {
    transform: scale(0);
  }
  to {
    transform: scale(1);
  }
}

@keyframes shake {
  0%, 100% {
    transform: translateX(0);
  }
  25% {
    transform: translateX(-10px);
  }
  75% {
    transform: translateX(10px);
  }
}
</style>
