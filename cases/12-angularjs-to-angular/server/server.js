// One small server for both applications: serves the files of an application and the API they both call.
//   node server/server.js --root before/app --port 4301 [--vendor before/node_modules]
// The data lives in memory and POST /api/_reset puts it back, so every test starts from the same state.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const args = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, i, all) => (value.startsWith('--') ? [...pairs, [value.slice(2), all[i + 1]]] : pairs), []));
const root = path.resolve(args.root);
const vendor = args.vendor ? path.resolve(args.vendor) : null;
const port = Number(args.port);

const seed = () => [
  { id: 1, customer: 'Anna Schmidt', city: 'Berlin', total: 1249.5, placed: '2026-03-02T09:15:00Z', status: 'new', discount: 0, note: 'Deliver to the <b>back entrance</b>.' },
  { id: 2, customer: 'bernd Meier', city: 'Hamburg', total: 89, placed: '2026-03-04T14:40:00Z', status: 'shipped', discount: 5, note: 'Call <a href="tel:+4940123456">the office</a> first.<script>window.hacked = true</script><img src="x" onerror="window.hacked = true">' },
  { id: 3, customer: 'Zoë Ärmel', city: 'berlin', total: 349, placed: '2026-02-11T08:00:00Z', status: 'new', discount: 10, note: '' },
  { id: 4, customer: 'carla Rossi', city: null, total: null, placed: '2026-03-01T16:05:00Z', status: 'cancelled', discount: 0, note: '' },
  { id: 5, customer: 'Émile Dubois', city: 'Lyon', total: 15.25, placed: '2026-01-20T11:30:00Z', status: 'shipped', discount: 0, note: '' },
];
let orders = seed();
let log = [];

const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json', '.ico': 'image/x-icon' };
const json = (response, status, body) => { response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' }); response.end(JSON.stringify(body)); };
const body = (request) => new Promise((resolve) => { let text = ''; request.on('data', (chunk) => (text += chunk)); request.on('end', () => resolve(text)); });

http.createServer(async (request, response) => {
  const url = new URL(request.url, 'http://localhost');

  if (url.pathname.startsWith('/api/')) {
    if (url.pathname === '/api/_reset') { orders = seed(); log = []; return json(response, 200, {}); }
    if (url.pathname === '/api/_log') return json(response, 200, log);
    log.push({ method: request.method, url: request.url, xsrf: request.headers['x-xsrf-token'] ?? null, body: request.method === 'PUT' ? await body(request) : null });

    const one = url.pathname.match(/^\/api\/orders\/(\d+)$/);
    if (url.pathname === '/api/orders' && request.method === 'GET') {
      const status = url.searchParams.get('status');
      return json(response, 200, status ? orders.filter((o) => o.status === status) : orders);
    }
    if (one && request.method === 'GET') {
      const order = orders.find((o) => o.id === Number(one[1]));
      return order ? json(response, 200, order) : json(response, 404, { error: 'Order not found' });
    }
    if (one && request.method === 'PUT') {
      // The cookie is set with the page. A request that changes data must send it back in a header.
      if (request.headers['x-xsrf-token'] !== 'token-123') return json(response, 403, { error: 'Missing XSRF token' });
      const order = orders.find((o) => o.id === Number(one[1]));
      if (!order) return json(response, 404, { error: 'Order not found' });
      Object.assign(order, JSON.parse(log.at(-1).body));
      return json(response, 200, order);
    }
    return json(response, 404, { error: 'Not found' });
  }

  // Static files. Anything that is not a file is the application itself, so that deep links work.
  let file = path.join(root, decodeURIComponent(url.pathname));
  if (vendor && url.pathname.startsWith('/vendor/')) file = path.join(vendor, decodeURIComponent(url.pathname.slice('/vendor/'.length)));
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) file = path.join(root, 'index.html');
  response.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream', 'Set-Cookie': 'XSRF-TOKEN=token-123; Path=/' });
  fs.createReadStream(file).pipe(response);
}).listen(port, '127.0.0.1', () => console.log(`serving ${root} on http://127.0.0.1:${port}`));
