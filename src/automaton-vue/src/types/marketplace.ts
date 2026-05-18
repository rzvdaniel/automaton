export type MarketplaceAppCard = {
  id: string
  name: string
  creatorPublicUsername?: string
  creatorDisplayName?: string
  summary?: string
  description?: string
  thumbnailUrl?: string
  category?: string
  tags?: string
  viewCount: number
  starCount: number
  forkCount: number
  isForkable: boolean
  isStarredByCurrentUser?: boolean
  publicUrl?: string
  publishedAt?: string
  updatedAt?: string
  isFeatured?: boolean
  isProfileFeatured?: boolean
  screenshots: MarketplaceAppScreenshot[]
}

export type MarketplaceAppScreenshot = {
  id: string
  imageUrl: string
  caption?: string
  sortOrder: number
}

export type MarketplaceAppDetails = MarketplaceAppCard & {
  relatedApps: MarketplaceAppCard[]
}

export type PublicUserProfile = {
  username: string
  displayName?: string
  avatarUrl?: string
  bio?: string
  publicAppCount: number
}

export type PublicAppsResponse = {
  profile: PublicUserProfile
  apps: MarketplaceAppCard[]
}
