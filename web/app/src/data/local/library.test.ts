import { describe, expect, it } from 'vitest';
import { isLocal, localUrl, sortTracks, toTrack, type LocalTrack } from './library';

const file = (over: Partial<LocalTrack>): LocalTrack => ({
  id: '0123456789abcdef',
  folder: 'C:\Music',
  file: 'a.mp3',
  title: 'Song',
  artist: null,
  album: null,
  albumArtist: null,
  track: null,
  year: null,
  durationSec: null,
  ...over,
});

describe('local library', () => {
  it('serves only well-formed local ids', () => {
    expect(localUrl('local:0123456789abcdef')).toBe('/local/0123456789abcdef');
    expect(localUrl('local:../../secret')).toBeNull();
    expect(localUrl('dQw4w9WgXcQ')).toBeNull();
    expect(isLocal('dQw4w9WgXcQ')).toBe(false);
  });

  it('makes a queue track, splitting Windows-joined artists', () => {
    const t = toTrack(file({ artist: 'A; B', album: 'X', durationSec: 61.6, track: 3 }));
    expect(t.videoId).toBe('local:0123456789abcdef');
    expect(t.artists).toEqual([
      { name: 'A', id: null },
      { name: 'B', id: null },
    ]);
    expect(t.album).toEqual({ name: 'X', id: null });
    expect(t.durationSec).toBe(62);
    expect(t.index).toBe(3);
    expect(toTrack(file({ albumArtist: 'Band' })).artists).toEqual([{ name: 'Band', id: null }]);
  });

  it('sorts by artist, album, then track number', () => {
    const sorted = sortTracks([
      file({ id: '1', artist: 'B', album: 'X', track: 1 }),
      file({ id: '2', artist: 'A', album: 'Y', track: 2 }),
      file({ id: '3', artist: 'A', album: 'Y', track: 1 }),
      file({ id: '4', artist: 'A', album: 'Y', track: null, title: 'Z' }),
    ]);
    expect(sorted.map((t) => t.id)).toEqual(['3', '2', '4', '1']);
  });
});
