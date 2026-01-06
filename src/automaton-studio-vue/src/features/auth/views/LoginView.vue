<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'

const email = ref('')
const password = ref('')
const rememberMe = ref(false)
</script>

<template>
  <div class="login-view flex align-items-center justify-content-center min-h-screen">
    <Card class="login-card w-full" style="max-width: 400px">
      <template #header>
        <div class="text-center pt-4">
          <i class="pi pi-bolt text-4xl mb-3" style="color: var(--p-primary-500)"></i>
          <h1 class="text-2xl font-bold m-0">Automaton Studio</h1>
          <p class="text-color-secondary mt-2">Sign in to your account</p>
        </div>
      </template>

      <template #content>
        <form class="flex flex-column gap-4" @submit.prevent>
          <div class="flex flex-column gap-2">
            <label for="email" class="font-medium">Email</label>
            <InputText
              id="email"
              v-model="email"
              type="email"
              placeholder="Enter your email"
              class="w-full"
            />
          </div>

          <div class="flex flex-column gap-2">
            <label for="password" class="font-medium">Password</label>
            <Password
              id="password"
              v-model="password"
              placeholder="Enter your password"
              :feedback="false"
              toggle-mask
              class="w-full"
              input-class="w-full"
            />
          </div>

          <div class="flex align-items-center justify-content-between">
            <div class="flex align-items-center gap-2">
              <Checkbox v-model="rememberMe" input-id="remember" binary />
              <label for="remember" class="text-sm">Remember me</label>
            </div>
            <a href="#" class="text-sm text-primary no-underline hover:underline">
              Forgot password?
            </a>
          </div>

          <Button type="submit" label="Sign In" class="w-full" />

          <div class="text-center">
            <span class="text-color-secondary text-sm">Don't have an account? </span>
            <router-link to="/register" class="text-primary no-underline hover:underline text-sm">
              Sign up
            </router-link>
          </div>
        </form>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.login-view {
  background-color: var(--p-surface-50);
}

:global(.app-dark) .login-view {
  background-color: var(--p-surface-950);
}

.login-card {
  margin: 1rem;
}
</style>
