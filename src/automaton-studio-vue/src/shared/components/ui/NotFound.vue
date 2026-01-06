<script setup lang="ts">
// 404 Not Found page
</script>

<template>
  <v-app>
    <v-main class="d-flex align-center justify-center bg-background">
      <v-container>
        <div class="text-center">
          <h1 class="text-h1 font-weight-bold text-primary mb-4">404</h1>
          <h2 class="text-h4 mb-4">Page Not Found</h2>
          <p class="text-body-1 text-medium-emphasis mb-6">
            The page you're looking for doesn't exist or has been moved.
          </p>
          <v-btn color="primary" to="/" prepend-icon="mdi-home">
            Go to Dashboard
          </v-btn>
        </div>
      </v-container>
    </v-main>
  </v-app>
</template>
