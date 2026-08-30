import { mkdir, copyFile, writeFile } from 'node:fs/promises';

await mkdir('out', { recursive: true });
await copyFile('server.mjs', 'out/server.mjs');
await writeFile('out/build-marker.txt', 'node-build-completed\n');
