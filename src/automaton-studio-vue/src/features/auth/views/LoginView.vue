<script setup lang="ts">
import { ref } from 'vue'

const email = ref('')
const password = ref('')
</script>

<template>
  <v-container class="fill-height" fluid>
    <v-row align="center" justify="center">
      <v-col cols="12" sm="8" md="4">
        <v-card class="elevation-12">
          <v-card-title class="text-h5 font-weight-bold pa-6">
            Sign In
          </v-card-title>
          
          <v-card-text class="pa-6">
            <p class="text-body-2 text-medium-emphasis mb-6">
              Sign in to your Automaton Studio account
            </p>

            <v-form>
              <v-text-field
                v-model="email"
                label="Email"
                type="email"
                variant="outlined"
                prepend-inner-icon="mdi-email"
                class="mb-4"
              />

              <v-text-field
                v-model="password"
                label="Password"
                type="password"
                variant="outlined"
                prepend-inner-icon="mdi-lock"
                class="mb-4"
              />

              <v-btn
                color="primary"
                size="large"
                block
                class="mb-4"
              >
                Sign In
              </v-btn>

              <v-divider class="my-4" />

              <div class="text-center">
                <p class="text-body-2 text-medium-emphasis">
                  Don't have an account?
                  <a href="/register" class="text-primary">Sign up</a>
                </p>
              </div>
            </v-form>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </v-container>
</template>
