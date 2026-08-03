import { describe, expect, it } from 'vitest'
import { getPublishVisibilityState } from './publish-visibility'

describe('PublishDialog visibility state', () => {
  it('preserves a private listed app when reopened', () => {
    expect(getPublishVisibilityState('private', 'listed')).toEqual({
      publishRuntime: false,
      listInMarketplace: true,
    })
  })

  it('preserves a private unlisted app when reopened', () => {
    expect(getPublishVisibilityState('private', 'unlisted')).toEqual({
      publishRuntime: false,
      listInMarketplace: false,
    })
  })
})
