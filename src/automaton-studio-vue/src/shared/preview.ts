/** Shows the Preview pill next to the logo. Set to false at 1.0 (the website has the same constant). */
export const SHOW_PREVIEW_BADGE = true

export function previewTitle(version?: string | null): string {
  return version ? `Preview · ${version}` : 'Preview'
}
