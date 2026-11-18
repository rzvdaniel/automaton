import { describe, expect, it } from 'vitest'
import { previewTitle } from './preview'

describe('previewTitle', () => {
  it('adds the running version', () => expect(previewTitle('0.2.110')).toBe('Preview · 0.2.110'))
  it('works without a version', () => expect(previewTitle(null)).toBe('Preview'))
})
