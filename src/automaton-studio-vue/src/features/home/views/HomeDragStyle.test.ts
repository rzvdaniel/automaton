// @vitest-environment node
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

// jsdom has no layout or computed animation, so the approved drag mockup values (#1490) are pinned in the source.
const source = readFileSync(new URL('./HomeView.vue', import.meta.url), 'utf8')

describe('Home drag matches the approved mockup', () => {
  it('styles the landing slot', () => {
    expect(source).toMatch(/--slot-accent: #ea580c; --slot-fill: #fff7ed;/)
    expect(source).toMatch(/--slot-accent: #f97316; --slot-fill: #2a1c12;/)
    expect(source).toMatch(/\.pin-slot \{[^}]*border-radius: 14px;[^}]*animation: slot-pulse 1\.4s/)
    expect(source).toMatch(/prefers-reduced-motion: reduce\)[\s\S]*\.pin-slot \{ animation: none; \}/)
  })
  it('styles the dragged card', () => {
    expect(source).toContain('rotate(1.2deg) scale(1.02)')
    expect(source).toContain('0 18px 40px rgb(0 0 0 / 0.18)')
  })
  it('lets a phone drag', () => {
    expect(source).not.toMatch(/if \(phone\.value \|\| event\.button/)
  })
})
