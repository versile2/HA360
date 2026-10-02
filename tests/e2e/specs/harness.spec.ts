// The harness's own checks: no browser and no app, they run in the same job and stop a broken fixture from passing every test behind it.
import { demoUrl, expect, test } from '../fixtures.js';

test.describe('harness', () => {
  test('demoUrl() writes the seven parameters in order and percent-encodes every value (CR2-008)', () => {
    expect(demoUrl()).toBe('?demo=1&style=demo-offline');
    expect(demoUrl({ path: 'driving/jester', now: '2026-10-01T08:00:00+05:30', style: 'osm bright', sheet: '80', layout: 'panel', variant: ['no-gps', 'low battery'], week: 2 })).toBe(
      'driving/jester?demo=1&now=2026-10-01T08%3A00%3A00%2B05%3A30&style=osm%20bright&sheet=80&layout=panel&variant=no-gps,low%20battery&week=2',
    );
    // A `+` that stays unencoded reaches the server as a space and the offset is lost; the encoded one survives a round trip.
    const query = new URL(demoUrl({ now: '2026-10-01T08:00:00+05:30' }), 'http://example.invalid/').searchParams;
    expect(query.get('now')).toBe('2026-10-01T08:00:00+05:30');
  });

  test('demoUrl() refuses a path that would leave the ingress prefix', () => {
    expect(() => demoUrl({ path: '/driving' })).toThrow(/starts with a slash/);
  });
});
