import { test } from 'node:test';
import assert from 'node:assert/strict';

import { streamEvents } from '../src/api.js';

/**
 * The reading half of the run's event stream.
 *
 * This is where a ten-minute silent build cost us a release: the daemon sent nothing at all while a
 * repository was building, this worker was shut down for being idle, and the log window sat frozen
 * at the last percentage it had heard. The daemon now sends a comment frame every ten seconds, so
 * the two things checked here are that such a frame is ignored rather than parsed, and that the
 * reader returns when the stream ends instead of hanging - which is what lets the caller reconnect.
 */

/** A response whose body yields the given chunks, as fetch would. */
function respondWith(...chunks) {
  const encoder = new TextEncoder();

  return {
    ok: true,
    body: {
      getReader() {
        let at = 0;

        return {
          read: async () =>
            at < chunks.length
              ? { value: encoder.encode(chunks[at++]), done: false }
              : { value: undefined, done: true },
        };
      },
    },
  };
}

function withFetch(response, body) {
  const previous = globalThis.fetch;
  globalThis.fetch = async () => response;

  return body().finally(() => {
    globalThis.fetch = previous;
  });
}

const connection = { port: 9876 };

test('a keepalive comment is ignored and the events around it are not', async () => {
  const seen = [];

  await withFetch(
    respondWith(
      ': keepalive\n\n',
      'data: {"kind":"output","text":"restoring"}\n\n',
      ': keepalive\n\n',
      ': keepalive\n\n',
      'data: {"kind":"finished","text":"all tasks finished"}\n\n',
    ),
    () => streamEvents('abc', connection, (event) => seen.push(event)),
  );

  assert.deepEqual(seen.map((e) => e.kind), ['output', 'finished']);
  assert.equal(seen[0].text, 'restoring');
});

test('an event split across two chunks arrives once and whole', async () => {
  const seen = [];

  await withFetch(
    respondWith('data: {"kind":"output","te', 'xt":"half a line"}\n\n'),
    () => streamEvents('abc', connection, (event) => seen.push(event)),
  );

  assert.deepEqual(seen, [{ kind: 'output', text: 'half a line' }]);
});

test('the reader returns when the stream ends, so the caller can reconnect', async () => {
  const seen = [];

  // No timeout around this on purpose: hanging here is the failure being tested for, and the test
  // runner's own timeout is what would catch it.
  await withFetch(
    respondWith('data: {"kind":"output","text":"then the worker died"}\n\n'),
    () => streamEvents('abc', connection, (event) => seen.push(event)),
  );

  assert.equal(seen.length, 1);
});

test('a line that is not an event does not become one', async () => {
  const seen = [];

  await withFetch(
    respondWith('event: ping\nid: 7\n\n', 'data: not json\n\n', 'data: {"kind":"output","text":"ok"}\n\n'),
    () => streamEvents('abc', connection, (event) => seen.push(event)),
  );

  assert.deepEqual(seen, [{ kind: 'output', text: 'ok' }]);
});
