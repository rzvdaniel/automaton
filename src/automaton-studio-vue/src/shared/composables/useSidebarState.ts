import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'

const STORAGE_KEY = 'app-sidebar-pinned'
const MOBILE_BREAKPOINT = 1024

const isPinned = ref(false)
const isHovered = ref(false)
const isMobileOpen = ref(false)
const viewportWidth = ref(typeof window !== 'undefined' ? window.innerWidth : 1440)

function loadPinnedPreference() {
  if (typeof window === 'undefined') {
    return
  }

  try {
    isPinned.value = localStorage.getItem(STORAGE_KEY) === 'true'
  } catch {
    isPinned.value = false
  }
}

function persistPinnedPreference() {
  if (typeof window === 'undefined') {
    return
  }

  try {
    localStorage.setItem(STORAGE_KEY, String(isPinned.value))
  } catch {
    // non-critical
  }
}

function handleResize() {
  if (typeof window === 'undefined') {
    return
  }

  viewportWidth.value = window.innerWidth
  if (viewportWidth.value >= MOBILE_BREAKPOINT) {
    isMobileOpen.value = false
  }
}

const isMobile = computed(() => viewportWidth.value < MOBILE_BREAKPOINT)

export function useSidebarState() {
  const route = useRoute()

  onMounted(() => {
    loadPinnedPreference()
    handleResize()
    window.addEventListener('resize', handleResize)
  })

  onUnmounted(() => {
    window.removeEventListener('resize', handleResize)
  })

  const prefersCollapsedWorkspace = computed(() => {
    const routeName = route.name as string | undefined
    return routeName === 'app-editor' || routeName === 'app-new'
  })

  const isDesktopExpanded = computed(() => {
    if (prefersCollapsedWorkspace.value) {
      return isHovered.value
    }

    return isPinned.value || isHovered.value
  })

  function setHovered(value: boolean) {
    if (isMobile.value) {
      return
    }
    isHovered.value = value
  }

  function togglePinned() {
    if (isMobile.value) {
      return
    }
    isPinned.value = !isPinned.value
    persistPinnedPreference()
  }

  function openMobile() {
    if (!isMobile.value) {
      return
    }
    isMobileOpen.value = true
  }

  function closeMobile() {
    isMobileOpen.value = false
  }

  function toggleMobile() {
    if (!isMobile.value) {
      return
    }
    isMobileOpen.value = !isMobileOpen.value
  }

  return {
    isPinned,
    isHovered,
    isMobile,
    isMobileOpen,
    isExpanded: computed(() => (isMobile.value ? isMobileOpen.value : isDesktopExpanded.value)),
    prefersCollapsedWorkspace,
    setHovered,
    togglePinned,
    openMobile,
    closeMobile,
    toggleMobile,
  }
}
