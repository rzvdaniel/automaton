<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'

const name = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const agreeTerms = ref(false)
</script>

<template>
  <div class="register-view flex align-items-center justify-content-center min-h-screen">
    <Card class="register-card w-full" style="max-width: 400px">
      <template #header>
        <div class="text-center pt-4">
          <i class="pi pi-bolt text-4xl mb-3" style="color: var(--p-primary-500)"></i>
          <h1 class="text-2xl font-bold m-0">Create Account</h1>
          <p class="text-color-secondary mt-2">Get started with Automaton Studio</p>
        </div>
      </template>

      <template #content>
        <form class="flex flex-column gap-4" @submit.prevent>
          <div class="flex flex-column gap-2">
            <label for="name" class="font-medium">Full Name</label>
            <InputText
              id="name"
              v-model="name"
              placeholder="Enter your name"
              class="w-full"
            />
          </div>

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
              placeholder="Create a password"
              toggle-mask
              class="w-full"
              input-class="w-full"
            />
          </div>

          <div class="flex flex-column gap-2">
            <label for="confirmPassword" class="font-medium">Confirm Password</label>
            <Password
              id="confirmPassword"
              v-model="confirmPassword"
              placeholder="Confirm your password"
              :feedback="false"
              toggle-mask
              class="w-full"
              input-class="w-full"
            />
          </div>

          <div class="flex align-items-center gap-2">
            <Checkbox v-model="agreeTerms" input-id="terms" binary />
            <label for="terms" class="text-sm">
              I agree to the
              <a href="#" class="text-primary no-underline hover:underline">Terms of Service</a>
              and
              <a href="#" class="text-primary no-underline hover:underline">Privacy Policy</a>
            </label>
          </div>

          <Button type="submit" label="Create Account" class="w-full" />

          <div class="text-center">
            <span class="text-color-secondary text-sm">Already have an account? </span>
            <router-link to="/login" class="text-primary no-underline hover:underline text-sm">
              Sign in
            </router-link>
          </div>
        </form>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.register-view {
  background-color: var(--p-surface-50);
}

:global(.app-dark) .register-view {
  background-color: var(--p-surface-950);
}

.register-card {
  margin: 1rem;
}
</style>
