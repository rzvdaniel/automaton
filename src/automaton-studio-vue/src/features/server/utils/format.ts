const GIB = 1024 ** 3

export function formatGiB(bytes: number): string {
  const value = bytes / GIB
  return `${value.toFixed(value >= 10 ? 0 : 1)} GB`
}
