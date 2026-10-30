import { onBeforeUnmount, onMounted } from 'vue'

// Pages using the shared site theme paint the whole body, including overscroll.
// Counted, so moving between two themed pages never drops the class in between.
let themedPages = 0

export function useSiteTheme() {
  onMounted(() => {
    themedPages++
    document.body.classList.add('site-theme')
  })
  onBeforeUnmount(() => {
    themedPages = Math.max(0, themedPages - 1)
    if (themedPages === 0) document.body.classList.remove('site-theme')
  })
}
