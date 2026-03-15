/** Escapes a value for safe embedding inside a JS single-quoted string literal inside a &lt;script&gt; block. */
export function jsStr(value: string): string {
  return value
    .replace(/\\/g, '\\\\')
    .replace(/'/g, "\\'")
    .replace(/\r?\n/g, '\\n')
    .replace(/<\//g, '<\\/') // prevent </script> from breaking out of the <script> tag
}
