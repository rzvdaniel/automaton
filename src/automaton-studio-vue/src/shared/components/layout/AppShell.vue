<script setup lang="ts">
import AppBar from './AppBar.vue'
import NavigationRail from './NavigationRail.vue'
</script>

<template>
  <v-app>
    <AppBar />
    <NavigationRail />
    <v-main>
      <v-container fluid class="pa-6">
        <router-view />
      </v-container>
    </v-main>
  </v-app>
</template>
