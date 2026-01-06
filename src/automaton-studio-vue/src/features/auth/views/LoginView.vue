<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'

const router = useRouter()
const email = ref('')
const password = ref('')
const showPassword = ref(false)
const loading = ref(false)

async function login() {
  loading.value = true
  // TODO: Implement actual login
  await new Promise(resolve => setTimeout(resolve, 1000))
  loading.value = false
  router.push('/')
}
</script>

<template>
  <v-app>
    <v-main class="d-flex align-center justify-center bg-background">
      <v-container class="fill-height" fluid>
        <v-row align="center" justify="center">
          <v-col cols="12" sm="8" md="6" lg="4">
            <!-- Logo -->
            <div class="text-center mb-8">
              <v-avatar color="primary" size="64" class="mb-4">
                <v-icon icon="mdi-robot" size="32" />
              </v-avatar>
              <h1 class="text-h4 font-weight-bold">Automaton</h1>
              <p class="text-body-1 text-medium-emphasis">Workflow Automation Platform</p>
            </div>

            <!-- Login Form -->
            <v-card class="pa-4">
              <v-card-title class="text-center pb-0">Sign In</v-card-title>
              <v-card-text>
                <v-form @submit.prevent="login">
                  <v-text-field
                    v-model="email"
                    label="Email"
                    type="email"
                    prepend-inner-icon="mdi-email"
                    class="mb-4"
                    required
                  />
                  <v-text-field
                    v-model="password"
                    label="Password"
                    :type="showPassword ? 'text' : 'password'"
                    prepend-inner-icon="mdi-lock"
                    :append-inner-icon="showPassword ? 'mdi-eye-off' : 'mdi-eye'"
                    class="mb-4"
                    required
                    @click:append-inner="showPassword = !showPassword"
                  />
                  <v-btn
                    type="submit"
                    color="primary"
                    block
                    size="large"
                    :loading="loading"
                  >
                    Sign In
                  </v-btn>
                </v-form>
              </v-card-text>
              <v-card-actions class="justify-center">
                <span class="text-body-2">Don't have an account?</span>
                <v-btn variant="text" color="primary" to="/register">
                  Register
                </v-btn>
              </v-card-actions>
            </v-card>

            <!-- Footer -->
            <div class="text-center mt-6 text-caption text-medium-emphasis">
              &copy; {{ new Date().getFullYear() }} Automaton. All rights reserved.
            </div>
          </v-col>
        </v-row>
      </v-container>
    </v-main>
  </v-app>
</template>
