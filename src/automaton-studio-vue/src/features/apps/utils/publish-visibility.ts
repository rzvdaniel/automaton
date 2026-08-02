export type RuntimeVisibility = 'private' | 'public'
export type CatalogVisibility = 'unlisted' | 'listed'

export interface PublishVisibilityState {
  publishRuntime: boolean
  listInMarketplace: boolean
}

export function getPublishVisibilityState(
  runtimeVisibility?: RuntimeVisibility,
  catalogVisibility?: CatalogVisibility,
): PublishVisibilityState {
  return {
    publishRuntime: runtimeVisibility !== 'private',
    listInMarketplace: catalogVisibility !== 'unlisted',
  }
}
