import { createApp } from 'vue'
import App from './App.vue'
import vuetify from './plugins/vuetify'
import router from './router'
import { pinia, useAppStore } from './shared/stores'

// Styles
import './assets/styles/main.css'

// Create app
const app = createApp(App)

// Install plugins
app.use(pinia)
app.use(router)
app.use(vuetify)

// Initialize theme from localStorage or system preference
const appStore = useAppStore()
appStore.initTheme()

// Mount app
app.mount('#app')
