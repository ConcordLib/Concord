/**
 * Concord docs worker.
 * Serves versioned documentation from R2.
 *
 * Routing:
 *   /                  -> latest version (redirect to /latest/)
 *   /latest/...        -> newest vX.Y docs from R2 key prefix "latest/"
 *   /unstable/...      -> tip-of-main docs from R2 key prefix "unstable/"
 *   /v0.4/...          -> docs for the v0.4 branch from R2 key prefix "v0.4/"
 *   /versions.json     -> list of available versions
 */

interface Env {
  DOCS: R2Bucket;
  LATEST_VERSION: string;
  DOCS_USER?: string;
  DOCS_PASS?: string;
}

function authorized(request: Request, env: Env): boolean {
  if (!env.DOCS_USER || !env.DOCS_PASS) return true;
  const header = request.headers.get('authorization');
  if (!header?.startsWith('Basic ')) return false;
  let decoded: string;
  try {
    decoded = atob(header.slice(6));
  } catch {
    return false;
  }
  const sep = decoded.indexOf(':');
  if (sep === -1) return false;
  return decoded.slice(0, sep) === env.DOCS_USER && decoded.slice(sep + 1) === env.DOCS_PASS;
}

const MIME_TYPES: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.eot': 'application/vnd.ms-fontobject',
  '.map': 'application/json; charset=utf-8',
  '.xml': 'application/xml; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
  '.webp': 'image/webp',
  '.gif': 'image/gif',
  '.pdf': 'application/pdf',
};

function getMimeType(path: string): string {
  const idx = path.lastIndexOf('.');
  if (idx === -1) return 'application/octet-stream';
  return MIME_TYPES[path.slice(idx)] ?? 'application/octet-stream';
}

/** Extract the version prefix and the remaining path from a URL. */
function parseRoute(urlPath: string): { version: string; rest: string } | null {
  // Strip leading slash
  let path = urlPath.startsWith('/') ? urlPath.slice(1) : urlPath;

  // Root path -> redirect to latest
  if (path === '' || path === '/') {
    return { version: 'latest', rest: 'index.html' };
  }

  // Check if first segment is a version: 'latest', 'unstable', or a vX.Y(.Z) prefix.
  const slashIdx = path.indexOf('/');
  const firstSeg = slashIdx === -1 ? path : path.slice(0, slashIdx);
  const isVersion =
    firstSeg === 'latest' || firstSeg === 'unstable' || /^v\d+\.\d+/.test(firstSeg);

  if (isVersion) {
    const rest = slashIdx === -1 ? 'index.html' : path.slice(slashIdx + 1);
    return { version: firstSeg, rest: rest || 'index.html' };
  }

  // No version prefix -> serve from latest
  return { version: 'latest', rest: path };
}

function jsonResponse(body: BodyInit): Response {
  return new Response(body, { headers: { 'content-type': 'application/json; charset=utf-8' } });
}

function htmlResponse(body: BodyInit): Response {
  return new Response(body, {
    headers: {
      'content-type': 'text/html; charset=utf-8',
      'cache-control': 'public, max-age=3600',
    },
  });
}

async function serveVersions(env: Env): Promise<Response> {
  const obj = await env.DOCS.get('versions.json');
  if (obj) return jsonResponse(obj.body);
  return jsonResponse(JSON.stringify({ latest: env.LATEST_VERSION, versions: [] }));
}

function redirectFor(path: string, origin: string): Response | null {
  if (path === '/') {
    return Response.redirect(new URL('/latest/', origin).toString(), 302);
  }
  if (/^\/(v\d+\.\d+(\.\d+)?|latest|unstable)$/.test(path)) {
    return Response.redirect(new URL(path + '/', origin).toString(), 302);
  }
  return null;
}

function toR2Key(rest: string): string {
  if (rest.endsWith('/')) return rest + 'index.html';
  if (!rest.includes('.')) return rest + '/index.html';
  return rest;
}

async function serveDoc(env: Env, version: string, r2Key: string): Promise<Response> {
  const obj = await env.DOCS.get(`${version}/${r2Key}`);
  if (obj) {
    const mime = getMimeType(r2Key);
    return new Response(obj.body, {
      headers: {
        'content-type': mime,
        'cache-control': mime.includes('text/html')
          ? 'public, max-age=3600'
          : 'public, max-age=86400',
      },
    });
  }

  const dirObj = await env.DOCS.get(`${version}/${r2Key.replace(/\/$/, '')}/index.html`);
  if (dirObj) return htmlResponse(dirObj.body);

  // DocFX emits the API reference root as Concord.html, not index.html, so a bare
  // /api/ directory request has no index. Fall back to the API landing page.
  if (/^api\/(index\.html)?$/.test(r2Key)) {
    const apiRootObj = await env.DOCS.get(`${version}/api/Concord.html`);
    if (apiRootObj) return htmlResponse(apiRootObj.body);
  }

  return new Response('Not found', { status: 404 });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    if (!authorized(request, env)) {
      return new Response('Unauthorized', {
        status: 401,
        headers: { 'www-authenticate': 'Basic realm="Concord Docs", charset="UTF-8"' },
      });
    }

    const url = new URL(request.url);
    const path = url.pathname;

    if (path === '/versions.json') {
      return serveVersions(env);
    }

    const redirect = redirectFor(path, url.origin);
    if (redirect) return redirect;

    const route = parseRoute(path);
    if (!route) {
      return new Response('Not found', { status: 404 });
    }

    return serveDoc(env, route.version, toR2Key(route.rest));
  },
};
