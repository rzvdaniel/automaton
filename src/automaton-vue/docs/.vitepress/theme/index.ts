import { h } from 'vue'
import DefaultTheme from 'vitepress/theme'
import CopyForAi from './CopyForAi.vue'

export default {
  extends: DefaultTheme,
  Layout: () => h(DefaultTheme.Layout, null, { 'doc-before': () => h(CopyForAi) }),
}
