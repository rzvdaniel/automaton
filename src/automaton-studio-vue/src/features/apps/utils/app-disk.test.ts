import { describe, expect, it } from 'vitest'
import { describeDisk, formatBytes } from './app-disk'

describe('app disk', () => {
  it('formats sizes', () => {
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(2048)).toBe('2 KB')
    expect(formatBytes(540 * 1024 ** 2)).toBe('540 MB')
    expect(formatBytes(1.25 * 1024 ** 3)).toBe('1.3 GB')
  })

  it('lists the measured parts and leaves out empty ones', () => {
    const text = describeDisk({
      appId: 'a', totalBytes: 1200 * 1024 ** 2, imageBytes: 540 * 1024 ** 2, dataBytes: 600 * 1024 ** 2,
      logsBytes: 60 * 1024 ** 2, filesBytes: 0, containerBytes: null,
    })
    expect(text).toBe('1.2 GB — image 540 MB, data 600 MB, logs 60 MB')
  })
})
