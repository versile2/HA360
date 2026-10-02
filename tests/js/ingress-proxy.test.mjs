// Tests for tests/e2e/harness/ingressProxy.mjs (03 section 7.5, 04 card S6a) against a stub upstream: prefix strip, the headers of
// HA's two hops, 404 outside the prefix, the ingress_session cookie, 401 on an upgrade without it, and the control endpoints.
// No browser and no .NET: the stub app records what it received and echoes websocket bytes.
import assert from 'node:assert/strict';
import http from 'node:http';
import net from 'node:net';
import { after, before, beforeEach, describe, test } from 'node:test';

import { CONTROL_PREFIX, DEMO_USER, INGRESS_PREFIX, INGRESS_TOKEN, SESSION_COOKIE, createIngressProxy } from '../e2e/harness/ingressProxy.mjs';

// ---- the stub app ----------------------------------------------------------------------------------------------------------------------

/** What the stub saw: { method, url, headers, body } per request, and the same for upgrades. */
let seen = [];
let upgraded = [];
let app;
let appPort;
let proxy;
let proxyPort;

function startApp() {
  const server = http.createServer((req, res) => {
    const chunks = [];
    req.on('data', (chunk) => chunks.push(chunk));
    req.on('end', () => {
      const body = Buffer.concat(chunks).toString('utf8');
      seen.push({ method: req.method, url: req.url, headers: req.headers, body });
      if (req.url === '/redirect') {
        res.writeHead(302, { Location: '/login' });
        return res.end();
      }
      if (req.url === '/cookie') {
        res.writeHead(200, { 'Set-Cookie': ['.AspNetCore.Antiforgery.abc=x; path=/api/hassio_ingress/token; samesite=strict; httponly'], 'Content-Type': 'text/plain' });
        return res.end('with a cookie');
      }
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ echoed: body }));
    });
  });
  // A websocket handshake answered by hand (no ws package): 101, then every byte comes back upper-cased.
  server.on('upgrade', (req, socket) => {
    upgraded.push({ method: req.method, url: req.url, headers: req.headers });
    socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: stub\r\n\r\n');
    socket.on('data', (chunk) => socket.write(chunk.toString('utf8').toUpperCase()));
    socket.on('end', () => socket.end()); // like a real server, answer the client's FIN with ours, so the tunnel can close
    socket.on('error', () => {});
  });
  return new Promise((resolve) => server.listen({ host: '127.0.0.1', port: 0 }, () => resolve(server)));
}

before(async () => {
  app = await startApp();
  appPort = app.address().port;
  proxy = createIngressProxy({ port: 0, upstreamPort: appPort });
  proxyPort = await proxy.listen();
});

after(async () => {
  await proxy.close();
  app.closeAllConnections();
  await new Promise((resolve) => app.close(resolve));
});

beforeEach(() => {
  seen = [];
  upgraded = [];
});

// ---- clients ---------------------------------------------------------------------------------------------------------------------------

/** One HTTP request to the proxy; resolves with { status, headers, rawHeaders, body }. */
function request(target, { method = 'GET', headers = {}, body } = {}, port = proxyPort) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port, method, path: target, headers, agent: false }, (res) => {
      const chunks = [];
      res.on('data', (chunk) => chunks.push(chunk));
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, rawHeaders: res.rawHeaders, body: Buffer.concat(chunks).toString('utf8') }));
    });
    req.on('error', reject);
    req.end(body);
  });
}

const through = (suffix, options, port) => request(`${INGRESS_PREFIX}${suffix}`, options, port);
const control = (name, method = 'POST') => request(`${CONTROL_PREFIX}${name}`, { method });

/** The ingress_session cookie the proxy issues on first contact: "ingress_session=<value>". */
async function openSession() {
  const response = await through('/');
  const cookie = [response.headers['set-cookie']].flat().find((c) => c?.startsWith(`${SESSION_COOKIE}=`));
  assert.ok(cookie, 'the first response sets ingress_session');
  return cookie.split(';')[0];
}

/**
 * A websocket-style upgrade at the proxy over a raw socket. Resolves with { statusLine, socket, received() } once the response head is in;
 * the socket stays open (the caller ends it).
 */
function upgrade(target, headers = {}) {
  return new Promise((resolve, reject) => {
    const socket = net.connect({ host: '127.0.0.1', port: proxyPort });
    let buffer = '';
    let settled = false;
    socket.on('error', (err) => {
      if (!settled) reject(err);
    });
    socket.on('data', (chunk) => {
      buffer += chunk.toString('utf8');
      const end = buffer.indexOf('\r\n\r\n');
      if (end !== -1 && !settled) {
        settled = true;
        const head = buffer.slice(0, end);
        buffer = buffer.slice(end + 4);
        resolve({ statusLine: head.split('\r\n')[0], head, socket, received: () => buffer });
      }
    });
    const lines = [`GET ${target} HTTP/1.1`, `Host: 127.0.0.1:${proxyPort}`, 'Connection: Upgrade', 'Upgrade: websocket', 'Sec-WebSocket-Version: 13', 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ=='];
    for (const [name, value] of Object.entries(headers)) lines.push(`${name}: ${value}`);
    socket.write(`${lines.join('\r\n')}\r\n\r\n`);
  });
}

const closed = (socket) => new Promise((resolve) => (socket.destroyed ? resolve() : socket.once('close', resolve)));

// ---- the prefix ------------------------------------------------------------------------------------------------------------------------

describe('prefix', () => {
  test('the constants: a 43-character token and a prefix without a trailing slash', () => {
    assert.equal(INGRESS_TOKEN.length, 43);
    assert.match(INGRESS_TOKEN, /^[A-Za-z0-9_-]+$/);
    assert.equal(INGRESS_PREFIX, `/api/hassio_ingress/${INGRESS_TOKEN}`);
    assert.equal(proxy.prefix, INGRESS_PREFIX);
  });

  test('strips the prefix and keeps the path and the query string exactly as sent', async () => {
    const response = await through('/_framework/blazor.web.js?x=1&y=%2B&z=a%20b');
    assert.equal(response.status, 200);
    assert.equal(seen.length, 1);
    assert.equal(seen[0].url, '/_framework/blazor.web.js?x=1&y=%2B&z=a%20b');
  });

  test('the root of the prefix is "/" for the app, with and without a query', async () => {
    await through('/');
    await through('/?demo=1&now=2026-09-30T21%3A25%3A00-05%3A00');
    assert.deepEqual(seen.map((s) => s.url), ['/', '/?demo=1&now=2026-09-30T21%3A25%3A00-05%3A00']);
  });

  test('forwards the method and the request body (a SignalR negotiate is a POST)', async () => {
    const response = await through('/_blazor/negotiate?negotiateVersion=1', { method: 'POST', headers: { 'Content-Type': 'text/plain', 'Content-Length': '5' }, body: 'hello' });
    assert.equal(response.status, 200);
    assert.deepEqual(JSON.parse(response.body), { echoed: 'hello' });
    assert.equal(seen[0].method, 'POST');
    assert.equal(seen[0].url, '/_blazor/negotiate?negotiateVersion=1');
  });

  test('answers 404 outside the prefix and never reaches the app', async () => {
    for (const target of ['/', '/healthz', '/driving', '/api/hassio_ingress/', '/api/hassio_ingress/other-token/', INGRESS_PREFIX, `${INGRESS_PREFIX}x/`, `/x${INGRESS_PREFIX}/`]) {
      const response = await request(target);
      assert.equal(response.status, 404, target);
    }
    assert.equal(seen.length, 0);
  });

  test('passes Location verbatim: no redirect is followed or rewritten', async () => {
    const response = await through('/redirect');
    assert.equal(response.status, 302);
    assert.equal(response.headers.location, '/login');
    assert.equal(seen.length, 1);
  });

  test('answers 502 "502: Bad Gateway" when the app is down', async () => {
    const dead = createIngressProxy({ port: 0, upstreamPort: 1 });
    const port = await dead.listen();
    try {
      const response = await through('/', {}, port);
      assert.equal(response.status, 502);
      assert.equal(response.body, '502: Bad Gateway');
    } finally {
      await dead.close();
    }
  });
});

// ---- the headers -----------------------------------------------------------------------------------------------------------------------

describe('headers', () => {
  test('adds the headers of HA Core and the Supervisor', async () => {
    await through('/', { headers: { 'X-Forwarded-Proto': 'https', 'X-Forwarded-Host': 'ha.example.invalid' } });
    const h = seen[0].headers;
    assert.equal(h['x-ingress-path'], INGRESS_PREFIX, 'no trailing slash');
    assert.equal(h['x-hass-source'], 'core.ingress');
    assert.equal(h['x-forwarded-proto'], 'https', 'an inbound value wins, as in Core');
    assert.equal(h['x-forwarded-host'], 'ha.example.invalid');
    assert.match(h['x-forwarded-for'], /^(?:::ffff:)?127\.0\.0\.1$/);
    assert.equal(h['x-remote-user-id'], DEMO_USER.id);
    assert.equal(h['x-remote-user-id'], 'demo-user-1');
    assert.equal(h['x-remote-user-name'], DEMO_USER.name);
    assert.equal(h['x-remote-user-display-name'], DEMO_USER.displayName);
    assert.equal(h.host, `127.0.0.1:${appPort}`);
  });

  test('defaults X-Forwarded-Proto to http and X-Forwarded-Host to the Host the client used', async () => {
    await through('/');
    assert.equal(seen[0].headers['x-forwarded-proto'], 'http');
    assert.equal(seen[0].headers['x-forwarded-host'], `127.0.0.1:${proxyPort}`);
  });

  test('replaces what a client could spoof and drops the Supervisor tokens', async () => {
    await through('/', {
      headers: {
        'X-Remote-User-Id': 'attacker',
        'X-Remote-User-Name': 'attacker',
        'X-Ingress-Path': '/somewhere/else',
        'X-Supervisor-Token': 'secret',
        'X-Hassio-Key': 'secret',
        'X-Hass-Source': 'forged',
      },
    });
    const h = seen[0].headers;
    assert.equal(h['x-remote-user-id'], 'demo-user-1');
    assert.equal(h['x-remote-user-name'], DEMO_USER.name);
    assert.equal(h['x-ingress-path'], INGRESS_PREFIX);
    assert.equal(h['x-hass-source'], 'core.ingress');
    assert.equal(h['x-supervisor-token'], undefined);
    assert.equal(h['x-hassio-key'], undefined);
  });

  test("drops the browser's Accept-Encoding, like Core, and adds no compression", async () => {
    const response = await through('/', { headers: { 'Accept-Encoding': 'gzip, deflate, br' } });
    assert.equal(seen[0].headers['accept-encoding'], undefined);
    assert.equal(response.headers['content-encoding'], undefined);
  });

  test('passes the browser cookie through to the app (the ingress_session cookie is not stripped)', async () => {
    const cookie = await openSession();
    seen = [];
    await through('/', { headers: { Cookie: `${cookie}; other=1` } });
    assert.equal(seen[0].headers.cookie, `${cookie}; other=1`);
  });
});

// ---- the session cookie ----------------------------------------------------------------------------------------------------------------

describe('ingress_session cookie', () => {
  test('is set on first contact, as HA sets it: path /api/hassio_ingress/, SameSite=Strict', async () => {
    const response = await through('/');
    const cookies = [response.headers['set-cookie']].flat().filter(Boolean);
    assert.equal(cookies.length, 1);
    assert.match(cookies[0], /^ingress_session=[0-9a-f]{32}; Path=\/api\/hassio_ingress\/; SameSite=Strict$/);
  });

  test('is not set again while the browser sends a valid one', async () => {
    const cookie = await openSession();
    const response = await through('/', { headers: { Cookie: cookie } });
    assert.equal(response.headers['set-cookie'], undefined);
  });

  test('a cookie the proxy never issued is no session: a new one is set', async () => {
    const response = await through('/', { headers: { Cookie: `${SESSION_COOKIE}=forged` } });
    assert.ok([response.headers['set-cookie']].flat().some((c) => c?.startsWith(`${SESSION_COOKIE}=`) && !c.includes('=forged')));
  });

  test("keeps the app's own Set-Cookie (the Blazor antiforgery cookie) next to it", async () => {
    const response = await through('/cookie');
    const cookies = [response.headers['set-cookie']].flat();
    assert.equal(cookies.length, 2);
    assert.ok(cookies.some((c) => c.startsWith('.AspNetCore.Antiforgery.abc=x;')));
    assert.ok(cookies.some((c) => c.startsWith(`${SESSION_COOKIE}=`)));
  });

  test('is not set on a request outside the prefix', async () => {
    const response = await request('/nope');
    assert.equal(response.headers['set-cookie'], undefined);
  });
});

// ---- websocket upgrades ----------------------------------------------------------------------------------------------------------------

describe('websocket upgrade', () => {
  test('with the session cookie: 101, prefix stripped, headers added, bytes piped both ways', async () => {
    const cookie = await openSession();
    const ws = await upgrade(`${INGRESS_PREFIX}/_blazor?id=abc123&x=%2B`, { Cookie: cookie, Origin: `http://127.0.0.1:${proxyPort}` });
    try {
      assert.equal(ws.statusLine, 'HTTP/1.1 101 Switching Protocols');
      assert.equal(upgraded.length, 1);
      assert.equal(upgraded[0].url, '/_blazor?id=abc123&x=%2B');
      assert.equal(upgraded[0].headers['x-ingress-path'], INGRESS_PREFIX);
      assert.equal(upgraded[0].headers['x-remote-user-id'], 'demo-user-1');
      assert.equal(upgraded[0].headers['sec-websocket-key'], 'dGhlIHNhbXBsZSBub25jZQ==');
      assert.match(upgraded[0].headers.connection, /upgrade/i);
      assert.equal(upgraded[0].headers.upgrade, 'websocket');
      assert.equal(upgraded[0].headers.cookie, cookie, 'the cookie reaches the app');

      ws.socket.write('ping');
      await new Promise((resolve) => ws.socket.once('data', resolve));
      assert.equal(ws.received(), 'PING');
    } finally {
      ws.socket.destroy();
    }
  });

  test('without the cookie: 401 and the app never sees it', async () => {
    const ws = await upgrade(`${INGRESS_PREFIX}/_blazor?id=abc123`);
    assert.match(ws.statusLine, /^HTTP\/1\.1 401 /);
    await closed(ws.socket);
    assert.equal(upgraded.length, 0);
  });

  test('with a cookie the proxy never issued: 401', async () => {
    const ws = await upgrade(`${INGRESS_PREFIX}/_blazor?id=abc123`, { Cookie: `${SESSION_COOKIE}=forged` });
    assert.match(ws.statusLine, /^HTTP\/1\.1 401 /);
    await closed(ws.socket);
    assert.equal(upgraded.length, 0);
  });

  test('outside the prefix: 404', async () => {
    const cookie = await openSession();
    const ws = await upgrade('/_blazor?id=abc123', { Cookie: cookie });
    assert.match(ws.statusLine, /^HTTP\/1\.1 404 /);
    await closed(ws.socket);
    assert.equal(upgraded.length, 0);
  });
});

// ---- control endpoints -----------------------------------------------------------------------------------------------------------------

describe('control endpoints', () => {
  test('GET /__proxy/health is 200 ok, outside the prefix', async () => {
    const response = await control('health', 'GET');
    assert.equal(response.status, 200);
    assert.equal(response.body, 'ok');
    assert.equal(seen.length, 0, 'the app is not involved');
  });

  test('POST /__proxy/drop-websockets closes every upgraded socket and counts them', async () => {
    const cookie = await openSession();
    const first = await upgrade(`${INGRESS_PREFIX}/_blazor?id=1`, { Cookie: cookie });
    const second = await upgrade(`${INGRESS_PREFIX}/_blazor?id=2`, { Cookie: cookie });
    assert.equal(first.statusLine.split(' ')[1], '101');
    assert.equal(second.statusLine.split(' ')[1], '101');

    const response = await control('drop-websockets');
    assert.equal(response.status, 200);
    assert.deepEqual(JSON.parse(response.body), { dropped: 2 });
    await Promise.all([closed(first.socket), closed(second.socket)]);

    assert.deepEqual(JSON.parse((await control('drop-websockets')).body), { dropped: 0 });
    // a reconnect with the same cookie still works: dropping a socket does not end the session
    const again = await upgrade(`${INGRESS_PREFIX}/_blazor?id=3`, { Cookie: cookie });
    assert.equal(again.statusLine.split(' ')[1], '101');
    again.socket.destroy();
  });

  test('POST /__proxy/expire-session answers 401 to the next upgrade only, then recovers', async () => {
    const cookie = await openSession();
    const response = await control('expire-session');
    assert.equal(response.status, 200);
    assert.deepEqual(JSON.parse(response.body), { rejecting: 1 });

    const rejected = await upgrade(`${INGRESS_PREFIX}/_blazor?id=1`, { Cookie: cookie });
    assert.match(rejected.statusLine, /^HTTP\/1\.1 401 /);
    await closed(rejected.socket);

    const recovered = await upgrade(`${INGRESS_PREFIX}/_blazor?id=2`, { Cookie: cookie });
    assert.equal(recovered.statusLine.split(' ')[1], '101');
    recovered.socket.destroy();
    assert.equal(upgraded.length, 1, 'only the second upgrade reached the app');
  });

  test('expire-session?upgrades=2 keeps the 401 window open for two attempts; plain HTTP is unaffected', async () => {
    const cookie = await openSession();
    assert.deepEqual(JSON.parse((await control('expire-session?upgrades=2')).body), { rejecting: 2 });
    assert.equal((await through('/', { headers: { Cookie: cookie } })).status, 200);
    for (let attempt = 1; attempt <= 2; attempt += 1) {
      const ws = await upgrade(`${INGRESS_PREFIX}/_blazor?id=${attempt}`, { Cookie: cookie });
      assert.match(ws.statusLine, /^HTTP\/1\.1 401 /, `attempt ${attempt}`);
      await closed(ws.socket);
    }
    const ws = await upgrade(`${INGRESS_PREFIX}/_blazor?id=3`, { Cookie: cookie });
    assert.equal(ws.statusLine.split(' ')[1], '101');
    ws.socket.destroy();
  });

  test('an unknown control path is 404 and a wrong method is 405', async () => {
    assert.equal((await control('nope')).status, 404);
    assert.equal((await control('health')).status, 405);
    assert.equal((await control('drop-websockets', 'GET')).status, 405);
    assert.equal((await control('expire-session', 'GET')).status, 405);
  });
});
