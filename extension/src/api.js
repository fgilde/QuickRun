// Talks to the QuickRun daemon on loopback.
//
// The daemon is the only channel that can tell us QuickRun is installed: a browser cannot be
// asked whether a quickrun:// handler exists, so the ping is what drives every button state.

const DEFAULT_PORT = 9876;

export async function settings() {
  const stored = await chrome.storage.local.get({
    port: DEFAULT_PORT,
    token: null,
    useProtocolFallback: true,
    preferMergeRef: false,
  });
  return stored;
}

export function baseUrl(port) {
  return `http://127.0.0.1:${port}`;
}

async function request(path, { method = 'GET', body, token, port, timeoutMs = 8000 } = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(`${baseUrl(port)}${path}`, {
      method,
      headers: {
        ...(body ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { 'X-QuickRun-Token': token } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    });

    const text = await response.text();
    const payload = text ? safeJson(text) : null;
    return { ok: response.ok, status: response.status, payload };
  } catch (error) {
    // A refused connection is the normal "not running" case, not an error worth surfacing raw.
    return { ok: false, status: 0, payload: null, offline: true, error: String(error) };
  } finally {
    clearTimeout(timer);
  }
}

function safeJson(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

/** Whether QuickRun is running, and which version. */
export async function ping(port) {
  const { ok, payload, offline } = await request('/api/ping', { port, timeoutMs: 1500 });
  if (!ok || !payload) return { running: false, offline: Boolean(offline) };
  return { running: true, version: payload.version, busy: Boolean(payload.busy) };
}

/** Claims a token; only succeeds while a pairing window is open on the machine. */
export async function pair(port) {
  const { ok, payload, status } = await request('/api/pair', { method: 'POST', port });
  if (ok && payload?.token) return { token: payload.token };
  return { error: payload?.error ?? `pairing failed (${status})` };
}

/** Prepares a run. Nothing executes: the daemon returns the plan for confirmation. */
export async function prepare(request_, { port, token }) {
  const { ok, payload, status } = await request('/api/run', {
    method: 'POST',
    body: request_,
    port,
    token,
    timeoutMs: 120000,
  });

  if (ok && payload) return { run: payload };
  return { error: payload?.error ?? `could not prepare the run (${status})`, run: payload?.run };
}

export async function confirm(runId, { port, token }) {
  const { ok, payload, status } = await request(`/api/runs/${runId}/confirm`, {
    method: 'POST',
    port,
    token,
  });
  return ok ? { run: payload } : { error: payload?.error ?? `could not start the run (${status})` };
}

export async function stop(runId, { port, token }) {
  const { ok } = await request(`/api/runs/${runId}/stop`, { method: 'POST', port, token });
  return ok;
}

export async function updateStatus({ port, token }) {
  const { ok, payload } = await request('/api/update', { port, token });
  return ok ? payload : null;
}

/**
 * Reads the run's Server-Sent Events stream.
 *
 * EventSource does not exist in an MV3 service worker, so the stream is read from fetch and the
 * SSE framing is parsed here.
 */
export async function streamEvents(runId, { port, token }, onEvent, signal) {
  const response = await fetch(`${baseUrl(port)}/api/runs/${runId}/events`, {
    headers: { 'X-QuickRun-Token': token },
    signal,
  });

  if (!response.ok || !response.body) return;

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    // Events are separated by a blank line.
    let boundary;
    while ((boundary = buffer.indexOf('\n\n')) !== -1) {
      const chunk = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);

      for (const line of chunk.split('\n')) {
        if (!line.startsWith('data:')) continue;
        const parsed = safeJson(line.slice(5).trim());
        if (parsed) onEvent(parsed);
      }
    }
  }
}
