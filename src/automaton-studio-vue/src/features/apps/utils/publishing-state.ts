import type { App } from '../types/apps.types'
import { getPublishedAppUrl } from './public-app-url'

function normalizeStatus(value: string | undefined): string {
  return value?.trim().toLocaleLowerCase() ?? ''
}

export function getPublicRuntimeUrl(app: App): string | null {
  if (
    !app.isPublished ||
    !app.publicUrl ||
    app.runtimeVisibility !== 'public' ||
    app.requiresAuthentication ||
    normalizeStatus(app.accessStatus) !== 'active'
  ) {
    return null
  }

  return getPublishedAppUrl(app.publicUrl)
}

export function getPublishingConsistencyIssue(app: App): string | null {
  if (app.isPublished) {
    if (!app.publicUrl) return 'The app is marked Published, but no public URL slug is configured.'
    if (!app.runtimeVisibility) return 'The app is marked Published, but runtime visibility is not configured.'
    if (app.runtimeVisibility === 'public' && app.requiresAuthentication) {
      return 'Public runtime visibility conflicts with the required sign-in setting.'
    }
    if (app.runtimeVisibility === 'private' && app.catalogVisibility === 'listed' && app.marketplacePreviewMode !== 'mediaOnly') {
      return 'A listed private runtime requires a media-only marketplace preview.'
    }
    if (app.runtimeVisibility === 'public' && normalizeStatus(app.accessStatus) !== 'active') {
      return 'Public runtime visibility is saved, but app access is not active.'
    }
    return null
  }

  if (app.runtimeVisibility === 'public' || app.catalogVisibility === 'listed') {
    return 'The app is marked Unpublished, but public visibility settings are still enabled.'
  }

  return null
}

export function getMarketplaceAvailability(app: App): string {
  if (app.catalogVisibility !== 'listed') return 'Not discoverable in the marketplace'
  if (!app.isPublished) return 'Listing is enabled while the app is unpublished; public availability needs review'
  if (normalizeStatus(app.reviewStatus) !== 'approved') return 'Listed setting saved; marketplace review is pending'
  if (normalizeStatus(app.accessStatus) !== 'active') return 'Listed setting saved; marketplace access is currently restricted'
  if (app.runtimeVisibility === 'private') return 'Discoverable with a media-only preview; runtime launch is private'
  if (app.marketplacePreviewMode === 'mediaOnly') return 'Discoverable with a media-only preview'
  return 'Discoverable in the marketplace'
}
