// Starts the test backend control server (tools/TestBackend) once for the whole run.
const { spawn, execFileSync } = require('node:child_process');
const net = require('node:net');
const path = require('node:path');

const freePort = () =>
  new Promise((resolve) => {
    const server = net.createServer();
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });

module.exports = async () => {
  const project = path.resolve(__dirname, '../../tools/TestBackend');
  execFileSync('dotnet', ['build', project, '-v', 'q', '--nologo'], { stdio: 'inherit' });

  const url = `http://127.0.0.1:${await freePort()}`;
  const child = spawn('dotnet', [path.join(project, 'bin/Debug/net10.0/TestBackend.dll'), '--urls', url], { stdio: 'ignore' });
  globalThis.__TEST_BACKEND__ = child;
  process.env.TEST_BACKEND_URL = url;

  for (let attempt = 0; attempt < 100; attempt++) {
    try {
      if ((await fetch(url)).ok) return;
    } catch {
      /* not listening yet */
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  throw new Error('The test backend did not start.');
};
