import http from 'node:http';

const port = Number(process.env.AUTOMATON_PORT);
const version = process.env.ACCEPTANCE_VERSION ?? 'unknown';
const healthDelayMs = Number(process.env.ACCEPTANCE_HEALTH_DELAY_MS ?? '0');

const server = http.createServer((request, response) => {
  if (request.url?.startsWith('/health')) {
    setTimeout(() => {
      response.writeHead(200, { 'content-type': 'text/plain' });
      response.end('ready');
    }, healthDelayMs);
    return;
  }

  response.writeHead(200, { 'content-type': 'text/plain' });
  response.end(`node:${version}:${request.url}:${process.env.AUTOMATON_APP_DATA_DIR}`);
});

server.listen(port, '127.0.0.1');
