import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import { publicAppConfig } from './config/publicAppConfig'
import './assets/main.css'
import './assets/site.css'

document.title = publicAppConfig.storeTitle

createApp(App).use(router).mount('#app')
