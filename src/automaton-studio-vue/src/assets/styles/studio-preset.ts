import Aura from '@primevue/themes/aura'
import { definePreset } from '@primevue/themes'

/**
 * The Aura preset mapped to the Studio design tokens (tokens.css): the signal orange as the primary colour, the
 * website's warm greys as the surface scale, 16px cards. Dark values apply under the .app-dark selector.
 */
export const StudioPreset = definePreset(Aura, {
  primitive: {
    borderRadius: { none: '0', xs: '4px', sm: '6px', md: '8px', lg: '12px', xl: '16px' },
  },
  semantic: {
    primary: {
      50: '#fff4ec',
      100: '#ffe4d1',
      200: '#ffc8a3',
      300: '#ffa36b',
      400: '#ff7a2e',
      500: '#f26a1b',
      600: '#d9590f',
      700: '#a23d05',
      800: '#7f3004',
      900: '#5e2403',
      950: '#3a1602',
    },
    colorScheme: {
      light: {
        surface: {
          0: '#ffffff',
          50: '#f3f2ee',
          100: '#ebe9e3',
          200: '#e2e0da',
          300: '#cfcdc6',
          400: '#a9aab1',
          500: '#8a8b92',
          600: '#55565e',
          700: '#3a3b42',
          800: '#23242a',
          900: '#17181c',
          950: '#0b0c0e',
        },
        primary: {
          color: '#f26a1b',
          contrastColor: '#17181c',
          hoverColor: '#d9590f',
          activeColor: '#a23d05',
        },
        highlight: {
          background: '#ffe4d1',
          focusBackground: '#ffc8a3',
          color: '#a23d05',
          focusColor: '#a23d05',
        },
      },
      dark: {
        surface: {
          0: '#ffffff',
          50: '#f1f0eb',
          100: '#d9d8d3',
          200: '#c3c2bd',
          300: '#a9aab1',
          400: '#8a8b92',
          500: '#6b6c73',
          600: '#4a4b52',
          700: '#2c2d33',
          800: '#23242a',
          900: '#1b1c20',
          950: '#121316',
        },
        primary: {
          color: '#ff7a2e',
          contrastColor: '#17181c',
          hoverColor: '#ffa36b',
          activeColor: '#ffc8a3',
        },
        highlight: {
          background: 'rgba(255, 122, 46, 0.16)',
          focusBackground: 'rgba(255, 122, 46, 0.24)',
          color: '#ffa36b',
          focusColor: '#ffa36b',
        },
      },
    },
  },
})
