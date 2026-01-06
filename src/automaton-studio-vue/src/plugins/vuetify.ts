// Vuetify Plugin Configuration
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import '@mdi/font/css/materialdesignicons.css'

// Use Vuetify's default Material Design 3 theme
export default createVuetify({
  components,
  directives,
  theme: {
    defaultTheme: 'light',
    themes: {
      light: {
        colors: {
          // Using Vuetify defaults - no custom colors
        }
      },
      dark: {
        colors: {
          // Using Vuetify defaults - no custom colors
        }
      }
    }
  },
  defaults: {
    VBtn: {
      color: 'primary',
      variant: 'elevated'
    },
    VCard: {
      elevation: 2
    }
  }
})
