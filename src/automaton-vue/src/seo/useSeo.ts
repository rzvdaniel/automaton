export type SeoMeta = {
  title: string
  description: string
  path: string
  image?: string
  noindex?: boolean
}

const siteName = 'Automaton Monster'
const baseUrl = 'https://automaton.monster'
const defaultImage = `${baseUrl}/og/automaton-monster-og.png`

function setMeta(nameOrProperty: string, value: string, property = false) {
  const attr = property ? 'property' : 'name'
  let tag = document.head.querySelector<HTMLMetaElement>(`meta[${attr}="${nameOrProperty}"]`)

  if (!tag) {
    tag = document.createElement('meta')
    tag.setAttribute(attr, nameOrProperty)
    document.head.appendChild(tag)
  }

  tag.setAttribute('content', value)
}

function setCanonical(url: string) {
  let link = document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]')

  if (!link) {
    link = document.createElement('link')
    link.setAttribute('rel', 'canonical')
    document.head.appendChild(link)
  }

  link.setAttribute('href', url)
}

export function applySeo(meta: SeoMeta) {
  const url = `${baseUrl}${meta.path}`
  const fullTitle = meta.title.includes(siteName) ? meta.title : `${meta.title} | ${siteName}`
  const image = meta.image ?? defaultImage

  document.title = fullTitle

  setMeta('description', meta.description)
  setCanonical(url)

  setMeta('og:type', 'website', true)
  setMeta('og:site_name', siteName, true)
  setMeta('og:title', fullTitle, true)
  setMeta('og:description', meta.description, true)
  setMeta('og:url', url, true)
  setMeta('og:image', image, true)

  setMeta('twitter:card', 'summary_large_image')
  setMeta('twitter:title', fullTitle)
  setMeta('twitter:description', meta.description)
  setMeta('twitter:image', image)

  setMeta('robots', meta.noindex ? 'noindex, nofollow' : 'index, follow')
}
