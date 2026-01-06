import 'vuetify/styles'
import '@mdi/font/css/materialdesignicons.css'
import { createVuetify, type ThemeDefinition } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'

/**
 * Automaton Studio Design System
 * Based on Material Design 3 principles
 * See: docs/vue-redesign/REDESIGN_PLAN.md
 */

// Light Theme - Primary palette with Blue and Deep Purple accent
const automatonLight: ThemeDefinition = {
  dark: false,
  colors: {
    // Primary Colors (Blue)
    primary: '#1976D2',           // Blue 700 - Main actions, navigation
    'primary-darken-1': '#0D47A1', // Blue 900 - Active states
    'primary-lighten-1': '#42A5F5', // Blue 400 - Hover states

    // Secondary Colors (Deep Purple - AI features)
    secondary: '#7C4DFF',          // Deep Purple A200 - AI features, highlights
    'secondary-darken-1': '#651FFF',
    'secondary-lighten-1': '#B47CFF',

    // Semantic Colors
    success: '#4CAF50',            // Green 500 - Completed, running
    'success-darken-1': '#388E3C',
    warning: '#FF9800',            // Orange 500 - Pending, attention
    'warning-darken-1': '#F57C00',
    error: '#F44336',              // Red 500 - Failed, errors
    'error-darken-1': '#D32F2F',
    info: '#2196F3',               // Blue 500 - Information
    'info-darken-1': '#1976D2',

    // Surface Colors
    background: '#FAFAFA',         // Grey 50 - Page background
    surface: '#FFFFFF',            // Cards, dialogs
    'surface-bright': '#FFFFFF',
    'surface-light': '#F5F5F5',    // Grey 100 - Secondary surfaces
    'surface-variant': '#F5F5F5',

    // Text Colors
    'on-background': '#212121',    // Grey 900 - Text on surfaces
    'on-surface': '#212121',
    'on-surface-variant': '#757575',
    'on-primary': '#FFFFFF',
    'on-secondary': '#FFFFFF',
    'on-success': '#FFFFFF',
    'on-warning': '#FFFFFF',
    'on-error': '#FFFFFF',
    'on-info': '#FFFFFF',
  },
  variables: {
    'border-color': '#BDBDBD',     // Grey 400 - Borders, dividers
    'border-opacity': 1,
    'high-emphasis-opacity': 0.87,
    'medium-emphasis-opacity': 0.60,
    'disabled-opacity': 0.38,
    'idle-opacity': 0.04,
    'hover-opacity': 0.04,
    'focus-opacity': 0.12,
    'selected-opacity': 0.08,
    'activated-opacity': 0.12,
    'pressed-opacity': 0.12,
    'dragged-opacity': 0.08,
  },
}

// Dark Theme
const automatonDark: ThemeDefinition = {
  dark: true,
  colors: {
    // Primary Colors
    primary: '#42A5F5',            // Blue 400
    'primary-darken-1': '#1976D2',
    'primary-lighten-1': '#64B5F6',

    // Secondary Colors (AI features)
    secondary: '#B47CFF',
    'secondary-darken-1': '#7C4DFF',
    'secondary-lighten-1': '#D1A3FF',

    // Semantic Colors
    success: '#66BB6A',
    'success-darken-1': '#4CAF50',
    warning: '#FFA726',
    'warning-darken-1': '#FF9800',
    error: '#EF5350',
    'error-darken-1': '#F44336',
    info: '#42A5F5',
    'info-darken-1': '#2196F3',

    // Surface Colors
    background: '#121212',
    surface: '#1E1E1E',
    'surface-bright': '#2D2D2D',
    'surface-light': '#2D2D2D',
    'surface-variant': '#2D2D2D',

    // Text Colors
    'on-background': '#E0E0E0',
    'on-surface': '#E0E0E0',
    'on-surface-variant': '#9E9E9E',
    'on-primary': '#000000',
    'on-secondary': '#000000',
    'on-success': '#000000',
    'on-warning': '#000000',
    'on-error': '#FFFFFF',
    'on-info': '#000000',
  },
  variables: {
    'border-color': '#424242',
    'border-opacity': 1,
    'high-emphasis-opacity': 0.87,
    'medium-emphasis-opacity': 0.60,
    'disabled-opacity': 0.38,
    'idle-opacity': 0.10,
    'hover-opacity': 0.08,
    'focus-opacity': 0.12,
    'selected-opacity': 0.12,
    'activated-opacity': 0.12,
    'pressed-opacity': 0.16,
    'dragged-opacity': 0.08,
  },
}

export default createVuetify({
  components,
  directives,
  theme: {
    defaultTheme: 'automatonLight',
    themes: {
      automatonLight,
      automatonDark,
    },
  },
  defaults: {
    // Cards - Border Radius: 12px
    VCard: {
      rounded: 'lg',
      elevation: 1,
    },
    // Buttons - Border Radius: 8px, Height: 36px default
    VBtn: {
      variant: 'flat',
      rounded: 'lg',
    },
    // Inputs - Outlined variant, Border Radius: 4px
    VTextField: {
      variant: 'outlined',
      density: 'comfortable',
      rounded: true,
    },
    VSelect: {
      variant: 'outlined',
      density: 'comfortable',
      rounded: true,
    },
    VTextarea: {
      variant: 'outlined',
      density: 'comfortable',
      rounded: true,
    },
    VAutocomplete: {
      variant: 'outlined',
      density: 'comfortable',
      rounded: true,
    },
    // Chips
    VChip: {
      rounded: 'lg',
    },
    // Alerts
    VAlert: {
      rounded: 'lg',
      variant: 'tonal',
    },
    // Dialogs
    VDialog: {
      rounded: 'xl',
    },
    // Navigation - Rail: 72px, Drawer: 256px
    VNavigationDrawer: {
      elevation: 0,
    },
    // App Bar - Height: 64px
    VAppBar: {
      elevation: 0,
      height: 64,
    },
    // List Items
    VList: {
      rounded: 'lg',
    },
    VListItem: {
      rounded: 'lg',
    },
    // Snackbar
    VSnackbar: {
      rounded: 'lg',
    },
  },
})
