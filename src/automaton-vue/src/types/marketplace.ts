export type MarketplaceAppCard = {
  id: string
  name: string
  summary?: string
  description?: string
  thumbnailUrl?: string
  category?: string
  tags?: string
  viewCount: number
  starCount: number
  isStarredByCurrentUser?: boolean
  publicUrl?: string
  publishedAt?: string
  isFeatured?: boolean
}

export type MarketplaceAppScreenshot = {
  id: string
  imageUrl: string
  caption?: string
  sortOrder: number
}

export type MarketplaceAppDetails = MarketplaceAppCard & {
  screenshots: MarketplaceAppScreenshot[]
  relatedApps: MarketplaceAppCard[]
}
