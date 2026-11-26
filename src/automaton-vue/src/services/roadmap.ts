export interface RoadmapItem { text: string; issue: string; url: string }
export interface RoadmapGroup { title: string; items: RoadmapItem[] }
export interface RoadmapSection { title: string; groups: RoadmapGroup[] }

// Reads ROADMAP.md: "## Now/Next/Later" sections, "### theme" groups, "- text ([#N](url))" items.
export function parseRoadmap(markdown: string): RoadmapSection[] {
  const sections: RoadmapSection[] = []
  for (const line of markdown.split(/\r?\n/)) {
    const section = /^## (.+)$/.exec(line)
    if (section) { sections.push({ title: (section[1] ?? '').trim(), groups: [] }); continue }
    const group = /^### (.+)$/.exec(line)
    const current = sections[sections.length - 1]
    if (group && current) { current.groups.push({ title: (group[1] ?? '').trim(), items: [] }); continue }
    const item = /^- (.+?) \(\[#(\d+)\]\((https:\/\/github\.com\/[^)\s]+)\)\)\s*$/.exec(line)
    const last = current?.groups[current.groups.length - 1]
    if (item && last) last.items.push({ text: item[1] ?? '', issue: item[2] ?? '', url: item[3] ?? '' })
  }
  return sections
}
