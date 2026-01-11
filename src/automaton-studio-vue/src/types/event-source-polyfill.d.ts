// Type declaration for event-source-polyfill
declare module 'event-source-polyfill' {
  export class EventSourcePolyfill extends EventTarget {
    constructor(url: string, eventSourceInitDict?: EventSourceInit)
    readonly url: string
    readonly readyState: number
    readonly withCredentials: boolean
    onerror: ((this: EventSource, ev: Event) => any) | null
    onmessage: ((this: EventSource, ev: MessageEvent) => any) | null
    onopen: ((this: EventSource, ev: Event) => any) | null
    close(): void
    addEventListener<K extends keyof EventSourceEventMap>(
      type: K,
      listener: (this: EventSource, ev: EventSourceEventMap[K]) => any,
      options?: boolean | AddEventListenerOptions,
    ): void
    addEventListener(
      type: string,
      listener: EventListenerOrEventListenerObject,
      options?: boolean | AddEventListenerOptions,
    ): void
    removeEventListener<K extends keyof EventSourceEventMap>(
      type: K,
      listener: (this: EventSource, ev: EventSourceEventMap[K]) => any,
      options?: boolean | EventListenerOptions,
    ): void
    removeEventListener(
      type: string,
      listener: EventListenerOrEventListenerObject,
      options?: boolean | EventListenerOptions,
    ): void
  }

  interface EventSourceInit {
    method?: string
    headers?: Record<string, string>
    body?: string | FormData
    withCredentials?: boolean
  }
}
