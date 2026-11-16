// A minimal fetch for the tests, on Node's own http module. The global fetch in the Expo test environment is a stub
// that makes no requests, and the tests are meant to reach a real service.
import { request } from 'node:http';

export const http = ((url: string, init: { method?: string; headers?: Record<string, string>; body?: string } = {}) =>
  new Promise((resolve, reject) => {
    const call = request(url, { method: init.method ?? 'GET', headers: init.headers }, (response) => {
      const chunks: Buffer[] = [];
      response.on('data', (chunk) => chunks.push(chunk));
      response.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        const status = response.statusCode ?? 0;
        resolve({ ok: status >= 200 && status < 300, status, text: async () => text, json: async () => JSON.parse(text) });
      });
    });
    call.on('error', reject);
    if (init.body) call.write(init.body);
    call.end();
  })) as unknown as typeof fetch;
