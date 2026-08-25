// Talks to the QuickRun daemon on loopback.
//
// The daemon is the only channel that can tell us QuickRun is installed: a browser cannot be
// asked whether a quickrun:// handler exists, so the ping is what drives every button state.

const DEFAULT_PORT = 9876;

export async function settings() {
  return chrome.storage.local.get({
    port: DEFAULT_PORT,
    useProtocolFallback: true,
  });
}

export function baseUrl(port) {
  return `http://127.0.0.1:${port}`;
}

async function request(path, { method = 'GET', body, port, timeoutMs = 8000 } = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(`${baseUrl(port)}${path}`, {
      method,
      // The daemon authorises by the Origin the browser attaches to this request, which the
      // extension cannot choose and a web page cannot forge. Nothing else identifies us.
      headers: body ? { 'Content-Type': 'application/json' } : {},
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

/** Prepares a run. Nothing executes: the daemon returns the plan for confirmation. */
export async function prepare(request_, { port }) {
  const { ok, payload, status } = await request('/api/run', {
    method: 'POST',
    body: request_,
    port,
    timeoutMs: 120000,
  });

  if (ok && payload) return { run: payload };
  return { error: payload?.error ?? `could not prepare the run (${status})`, run: payload?.run };
}

/**
 * Supplies the values a config's inputs were missing. The daemon plans again with them, so the
 * command list that comes back is the one those values produced.
 */
export async function supplyInputs(runId, inputs, { port }) {
  const { ok, payload, status } = await request(`/api/runs/${runId}/inputs`, {
    method: 'POST',
    body: { inputs },
    port,
    timeoutMs: 120000,
  });

  if (ok && payload) return { run: payload };
  return { error: payload?.error ?? `could not apply the values (${status})`, run: payload?.run };
}

export async function confirm(runId, { port }) {
  const { ok, payload, status } = await request(`/api/runs/${runId}/confirm`, {
    method: 'POST',
    port,
  });
  return ok ? { run: payload } : { error: payload?.error ?? `could not start the run (${status})` };
}

export async function stop(runId, { port }) {
  const { ok } = await request(`/api/runs/${runId}/stop`, { method: 'POST', port });
  return ok;
}

/** Asks the daemon to open the run's workspace in the file manager. */
export async function reveal(runId, { port }) {
  const { ok } = await request(`/api/runs/${runId}/reveal`, { method: 'POST', port });
  return ok;
}

export async function updateStatus({ port }) {
  const { ok, payload } = await request('/api/update', { port });
  return ok ? payload : null;
}

/**
 * Reads the run's Server-Sent Events stream.
 *
 * EventSource does not exist in an MV3 service worker, so the stream is read from fetch and the
 * SSE framing is parsed here.
 */
export async function streamEvents(runId, { port }, onEvent, signal) {
  const response = await fetch(`${baseUrl(port)}/api/runs/${runId}/events`, { signal });

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
