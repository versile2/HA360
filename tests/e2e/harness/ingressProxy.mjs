// ingressProxy.mjs: a tiny stand-in for Home Assistant Core plus the Supervisor, so that the base-path, forwarded-header and websocket
// behaviour that only exists behind Ingress is exercised on every push (03 section 7.5, research ha-addon-ingress section 2).
//
//   node tests/e2e/harness/ingressProxy.mjs        (PROXY_PORT, APP_PORT, PROXY_LOG=1 are the only knobs)
//
//   browser -> http://127.0.0.1:8123/api/hassio_ingress/<token>/<path>?<query>
//   proxy   -> http://127.0.0.1:8099/<path>?<query>      with the prefix stripped and the headers of HA's two hops added
//
// What it does, in the order the real chain does it:
//   - only /api/hassio_ingress/<token>/... is proxied; every other path is a 404 (HA's own router), including the prefix without its
//     trailing slash and any other token
//   - strips inbound X-Ingress-Path, X-Forwarded-*, X-Remote-User-*, X-Hass-Source and X-Supervisor-Token (the Supervisor removes the
//     spoofable ones), then adds X-Ingress-Path (no trailing slash), X-Hass-Source, X-Forwarded-For/-Host/-Proto and X-Remote-User-*
//     (the name headers are sent for realism; the app ignores them, 03 section 5.5)
//   - drops Accept-Encoding (Core does), so the app answers uncompressed; the proxy adds no compression
//   - passes Location and every other response header verbatim (no redirect is followed or rewritten) and the app's own Set-Cookie
//   - sets the ingress_session cookie on first contact (the HA frontend does it in the real chain) and answers 401 to a websocket
//     upgrade that has no valid one
//   - pipes websocket upgrades byte for byte to the app
//   - control endpoints for tests, outside the prefix:
//       GET  /__proxy/health                      200 "ok"
//       POST /__proxy/drop-websockets             closes every upgraded socket; JSON { dropped: n }
//       POST /__proxy/expire-session[?upgrades=N] the next N upgrades (default 1) answer 401, like the window between a session expiring
//                                                 and the HA frontend's next keep-alive (research section 2.5); JSON { rejecting: N }
//
// node:http, node:net and node:crypto only. Everything else (the constants below, createIngressProxy) is exported for the Playwright config,
// the fixtures and tests/js/ingress-proxy.test.mjs.

import crypto from 'node:crypto';
import http from 'node:http';
import net from 'node:net';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

// A fixed 43-character token, the length of a real Ingress token (the same one tools/ci/image-smoke.sh uses).
export const INGRESS_TOKEN = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE';
/** The prefix the browser sees and the app receives in X-Ingress-Path: no trailing slash. */
export const INGRESS_PREFIX = `/api/hassio_ingress/${INGRESS_TOKEN}`;
export const PROXY_HOST = '127.0.0.1';
export const PROXY_PORT = 8123;
export const APP_HOST = '127.0.0.1';
export const APP_PORT = 8099;
export const SESSION_COOKIE = 'ingress_session';
/** The cookie's Path, as HA's frontend sets it. */
export const SESSION_COOKIE_PATH = '/api/hassio_ingress/';
export const CONTROL_PREFIX = '/__proxy/';
/** `demo-user-1` is the id DemoCast maps to `king` (03 section 7.5). The names are the fictional cast. */
export const DEMO_USER = Object.freeze({ id: 'demo-user-1', name: 'alden', displayName: 'Alden' });

// Hop-by-hop headers (RFC 9110 section 7.6.1), which a proxy never forwards (the upgrade pair is written explicitly for websockets).
const HOP_BY_HOP = new Set(['connection', 'keep-alive', 'proxy-authenticate', 'proxy-authorization', 'te', 'trailer', 'transfer-encoding', 'upgrade']);
// What the proxy sets itself, or what the real chain removes from a client request.
const REPLACED = new Set([
  'host',
  'accept-encoding',
  'x-ingress-path',
  'x-hass-source',
  'x-supervisor-token',
  'x-hassio-key',
  'x-forwarded-for',
  'x-forwarded-host',
  'x-forwarded-proto',
  'x-remote-user-id',
  'x-remote-user-name',
  'x-remote-user-display-name',
]);

/**
 * @typedef {object} IngressProxyOptions
 * @property {string} [host] address to listen on (default 127.0.0.1)
 * @property {number} [port] port to listen on; 0 picks a free one (default 8123)
 * @property {string} [upstreamHost] the app's address (default 127.0.0.1)
 * @property {number} [upstreamPort] the app's port (default 8099)
 * @property {string} [token] the ingress token (default INGRESS_TOKEN)
 * @property {{ id: string, name: string, displayName: string }} [user] the HA user the session carries
 * @property {(line: string) => void} [log] one line per proxied request or control call (default: silent)
 */

/**
 * @param {string | undefined} header the Cookie request header
 * @param {string} name
 * @returns {string | null}
 */
function cookieValue(header, name) {
  for (const part of (header ?? '').split(';')) {
    const at = part.indexOf('=');
    if (at > 0 && part.slice(0, at).trim() === name) return part.slice(at + 1).trim();
  }
  return null;
}

/**
 * Splits a raw request target into its path and query without decoding or normalising either (the app must see what the browser sent).
 * @param {string} target
 * @returns {{ pathname: string, search: string }}
 */
function splitTarget(target) {
  const at = target.indexOf('?');
  return at === -1 ? { pathname: target, search: '' } : { pathname: target.slice(0, at), search: target.slice(at) };
}

/**
 * @param {IngressProxyOptions} [options]
 */
export function createIngressProxy(options = {}) {
  const host = options.host ?? PROXY_HOST;
  const upstreamHost = options.upstreamHost ?? APP_HOST;
  const upstreamPort = options.upstreamPort ?? APP_PORT;
  const prefix = `/api/hassio_ingress/${options.token ?? INGRESS_TOKEN}`;
  const user = options.user ?? DEMO_USER;
  const log = options.log ?? (() => {});

  /** Sessions this proxy issued; a cookie that is not in the set is no session. */
  const sessions = new Set();
  let rejectUpgrades = 0;
  /** @type {Set<{ client: net.Socket, upstream: net.Socket }>} */
  const tunnels = new Set();

  /** @param {string | undefined} cookieHeader */
  const hasSession = (cookieHeader) => {
    const value = cookieValue(cookieHeader, SESSION_COOKIE);
    return value !== null && sessions.has(value);
  };

  const newSessionCookie = () => {
    const value = crypto.randomBytes(16).toString('hex');
    sessions.add(value);
    // The real cookie is set by the HA frontend's script, so no HttpOnly.
    return `${SESSION_COOKIE}=${value}; Path=${SESSION_COOKIE_PATH}; SameSite=Strict`;
  };

  /**
   * The path the app sees, or null when the request is not for this ingress.
   * @param {string} pathname
   */
  const stripPrefix = (pathname) => (pathname.startsWith(`${prefix}/`) ? pathname.slice(prefix.length) : null);

  /**
   * The headers of the two hops, for the request that goes to the app.
   * @param {http.IncomingMessage} req
   * @param {{ keepUpgrade: boolean }} mode a websocket keeps its Connection and Upgrade pair, a plain request drops it
   * @returns {[string, string][]}
   */
  function upstreamHeaders(req, { keepUpgrade }) {
    /** @type {[string, string][]} */
    const out = [];
    for (let i = 0; i < req.rawHeaders.length; i += 2) {
      const name = req.rawHeaders[i];
      const lower = name.toLowerCase();
      if (REPLACED.has(lower)) continue;
      if (HOP_BY_HOP.has(lower) && !(keepUpgrade && (lower === 'connection' || lower === 'upgrade'))) continue;
      out.push([name, req.rawHeaders[i + 1]]);
    }
    const proto = req.headers['x-forwarded-proto'];
    const forwardedHost = req.headers['x-forwarded-host'];
    out.push(
      ['Host', `${upstreamHost}:${upstreamPort}`],
      ['X-Ingress-Path', prefix],
      ['X-Hass-Source', 'core.ingress'],
      ['X-Forwarded-For', req.socket.remoteAddress ?? '127.0.0.1'],
      ['X-Forwarded-Host', String(forwardedHost ?? req.headers.host ?? host)],
      ['X-Forwarded-Proto', String(proto ?? 'http')],
      ['X-Remote-User-Id', user.id],
      ['X-Remote-User-Name', user.name],
      ['X-Remote-User-Display-Name', user.displayName],
    );
    return out;
  }

  /**
   * @param {http.ServerResponse} res
   * @param {number} status
   * @param {string} body
   * @param {string} [type]
   */
  function reply(res, status, body, type = 'text/plain; charset=utf-8') {
    res.writeHead(status, { 'Content-Type': type, 'Content-Length': Buffer.byteLength(body), 'Cache-Control': 'no-store' });
    res.end(body);
  }

  /**
   * @param {http.IncomingMessage} req
   * @param {http.ServerResponse} res
   * @param {string} name
   * @param {string} search
   */
  function control(req, res, name, search) {
    const post = req.method === 'POST';
    if (name === 'health') {
      if (req.method === 'GET' || req.method === 'HEAD') return reply(res, 200, 'ok');
    } else if (name === 'drop-websockets') {
      if (post) {
        const dropped = tunnels.size;
        for (const { client, upstream } of [...tunnels]) {
          client.destroy();
          upstream.destroy();
        }
        tunnels.clear();
        log(`control drop-websockets: ${dropped}`);
        return reply(res, 200, JSON.stringify({ dropped }), 'application/json');
      }
    } else if (name === 'expire-session') {
      if (post) {
        const asked = Number(new URLSearchParams(search).get('upgrades') ?? '1');
        rejectUpgrades = Number.isInteger(asked) && asked > 0 ? asked : 1;
        log(`control expire-session: next ${rejectUpgrades} upgrade(s) answer 401`);
        return reply(res, 200, JSON.stringify({ rejecting: rejectUpgrades }), 'application/json');
      }
    } else {
      return reply(res, 404, 'Not Found');
    }
    res.setHeader('Allow', name === 'health' ? 'GET, HEAD' : 'POST');
    return reply(res, 405, 'Method Not Allowed');
  }

  /**
   * @param {http.IncomingMessage} req
   * @param {http.ServerResponse} res
   */
  function onRequest(req, res) {
    const { pathname, search } = splitTarget(req.url ?? '/');
    if (pathname.startsWith(CONTROL_PREFIX)) return control(req, res, pathname.slice(CONTROL_PREFIX.length), search);
    const upstreamPath = stripPrefix(pathname);
    if (upstreamPath === null) return reply(res, 404, '404: Not Found');

    const issued = hasSession(req.headers.cookie) ? null : newSessionCookie();
    log(`${req.method} ${pathname}${search} -> ${upstreamPath}${search}`);
    // A flat [name, value, name, value, ...] list, the shape of rawHeaders: repeated headers and the order survive.
    const headers = upstreamHeaders(req, { keepUpgrade: false }).flat();
    /** @type {http.ClientRequest} */
    let proxied;
    try {
      proxied = http.request({ host: upstreamHost, port: upstreamPort, method: req.method, path: upstreamPath + search, headers, agent: false });
    } catch (err) {
      log(`request not sent: ${err instanceof Error ? err.message : err}`);
      return reply(res, 502, '502: Bad Gateway');
    }
    proxied.on('response', (upstream) => {
      /** @type {string[]} */
      const raw = [];
      for (let i = 0; i < upstream.rawHeaders.length; i += 2) {
        if (!HOP_BY_HOP.has(upstream.rawHeaders[i].toLowerCase())) raw.push(upstream.rawHeaders[i], upstream.rawHeaders[i + 1]);
      }
      if (issued !== null) raw.push('Set-Cookie', issued);
      res.writeHead(upstream.statusCode ?? 502, upstream.statusMessage, raw);
      upstream.pipe(res);
    });
    // The HA frontend treats a body of exactly "502: Bad Gateway" as "the app is not ready yet".
    proxied.on('error', () => {
      if (res.headersSent) res.destroy();
      else reply(res, 502, '502: Bad Gateway');
    });
    res.on('close', () => proxied.destroy());
    req.pipe(proxied);
  }

  /**
   * @param {net.Socket} socket
   * @param {number} status
   * @param {string} text
   */
  function refuse(socket, status, text) {
    socket.end(`HTTP/1.1 ${status} ${text}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n`);
  }

  /**
   * @param {http.IncomingMessage} req
   * @param {net.Socket} client
   * @param {Buffer} head
   */
  function onUpgrade(req, client, head) {
    const { pathname, search } = splitTarget(req.url ?? '/');
    const upstreamPath = stripPrefix(pathname);
    if (upstreamPath === null) return refuse(client, 404, 'Not Found');
    if (!hasSession(req.headers.cookie)) {
      log(`UPGRADE ${pathname}: 401 (no ingress_session)`);
      return refuse(client, 401, 'Unauthorized');
    }
    if (rejectUpgrades > 0) {
      rejectUpgrades -= 1;
      log(`UPGRADE ${pathname}: 401 (session expired by the test)`);
      return refuse(client, 401, 'Unauthorized');
    }

    log(`UPGRADE ${pathname}${search} -> ${upstreamPath}${search}`);
    const upstream = net.connect({ host: upstreamHost, port: upstreamPort });
    const tunnel = { client, upstream };
    const end = () => {
      tunnels.delete(tunnel);
      client.destroy();
      upstream.destroy();
    };
    client.on('error', end);
    upstream.on('error', () => {
      if (tunnels.has(tunnel)) return end();
      // refused before the tunnel was up: the client is still waiting for an HTTP answer, which refuse() flushes before it closes
      refuse(client, 502, 'Bad Gateway');
      upstream.destroy();
    });
    client.on('close', end);
    upstream.on('close', end);
    upstream.on('connect', () => {
      tunnels.add(tunnel);
      const lines = [`${req.method} ${upstreamPath}${search} HTTP/1.1`, ...upstreamHeaders(req, { keepUpgrade: true }).map(([name, value]) => `${name}: ${value}`)];
      upstream.write(`${lines.join('\r\n')}\r\n\r\n`);
      if (head.length > 0) upstream.write(head);
      client.pipe(upstream);
      upstream.pipe(client);
    });
  }

  const server = http.createServer(onRequest);
  server.on('upgrade', onUpgrade);

  return {
    server,
    /** The prefix the app receives in X-Ingress-Path. */
    prefix,
    /**
     * Starts listening and resolves with the port (useful with port 0).
     * @returns {Promise<number>}
     */
    listen() {
      return new Promise((resolve, reject) => {
        server.once('error', reject);
        server.listen({ host, port: options.port ?? PROXY_PORT }, () => {
          server.off('error', reject);
          resolve(/** @type {net.AddressInfo} */ (server.address()).port);
        });
      });
    },
    /**
     * Closes the listener and every open connection.
     * @returns {Promise<void>}
     */
    close() {
      return new Promise((resolve) => {
        for (const { client, upstream } of tunnels) {
          client.destroy();
          upstream.destroy();
        }
        tunnels.clear();
        server.close(() => resolve());
        server.closeAllConnections();
      });
    },
  };
}

/** @param {string | undefined} text @param {number} fallback */
function portFrom(text, fallback) {
  const n = Number(text);
  return text !== undefined && text !== '' && Number.isInteger(n) && n >= 0 && n < 65536 ? n : fallback;
}

async function main() {
  const proxy = createIngressProxy({
    port: portFrom(process.env.PROXY_PORT, PROXY_PORT),
    upstreamPort: portFrom(process.env.APP_PORT, APP_PORT),
    log: process.env.PROXY_LOG ? (line) => console.log(`[ingress-proxy] ${line}`) : undefined,
  });
  const port = await proxy.listen();
  console.log(`[ingress-proxy] http://${PROXY_HOST}:${port}${proxy.prefix}/ -> http://${APP_HOST}:${portFrom(process.env.APP_PORT, APP_PORT)}/`);
  const stop = () => void proxy.close().then(() => process.exit(0));
  process.on('SIGTERM', stop);
  process.on('SIGINT', stop);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main().catch((err) => {
    console.error(err);
    process.exit(1);
  });
}
