<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from 'vuetify'

const theme = useTheme()

const toggleTheme = () => {
  theme.global.name.value = theme.global.current.value.dark ? 'light' : 'dark'
}

const isDark = computed(() => theme.global.current.value.dark)
</script>

<template>
  <v-app-bar elevation="0" color="surface" border>
    <template #prepend>
      <v-app-bar-title class="text-h6 font-weight-bold ml-4">
        Automaton Studio
      </v-app-bar-title>
    </template>

    <v-spacer />

    <template #append>
      <v-btn :icon="isDark ? 'mdi-white-balance-sunny' : 'mdi-weather-night'" @click="toggleTheme">
        <v-icon>{{ isDark ? 'mdi-white-balance-sunny' : 'mdi-weather-night' }}</v-icon>
        <v-tooltip activator="parent" location="bottom">
          {{ isDark ? 'Light mode' : 'Dark mode' }}
        </v-tooltip>
      </v-btn>
      <v-btn icon="mdi-account-circle" class="mr-2">
        <v-icon>mdi-account-circle</v-icon>
        <v-tooltip activator="parent" location="bottom">Profile</v-tooltip>
      </v-btn>
    </template>
  </v-app-bar>
</template>
