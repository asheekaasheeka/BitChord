import { create } from 'zustand';
import type { Track } from '@/data/music/types';

/**
 * The Windows app's local music (web/windows: Local/LocalBridge.cs), over WebView2's chrome.webview.
 * The app scans the folders the user picked and serves each file at /local/<id>; the page only ever
 * sees ids and tags. A local track is an ordinary queue Track whose videoId is `local:<id>`.
 */

interface WebView {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (e: { data: unknown }) => void): void;
}

const webview: WebView | null = (globalThis as { chrome?: { webview?: WebView } }).chrome?.webview ?? null;

/** True inside the Windows app, the only host that can read local folders. */
export const localSupported = webview !== null;

const PREFIX = 'local:';
const ID = /^[0-9a-f]{16}$/;

export const isLocal = (videoId: string | null | undefined): boolean => !!videoId?.startsWith(PREFIX);

/** Where the app serves a local track, or null for anything that isn't one. */
export function localUrl(videoId: string): string | null {
  const id = videoId.slice(PREFIX.length);
  return isLocal(videoId) && ID.test(id) ? `/local/${id}` : null;
}

export interface LocalTrack {
  id: string;
  folder: string;
  /** Relative to its folder, for telling same-named files apart. */
  file: string;
  title: string;
  artist: string | null;
  album: string | null;
  albumArtist: string | null;
  track: number | null;
  year: number | null;
  durationSec: number | null;
  /** The file's own format, as Windows reads it (null when it couldn't). */
  codec: string | null;
  bitrateKbps: number | null;
  sampleRateHz: number | null;
  bitDepth: number | null;
  channels: number | null;
}

/** The library's entry for a local queue track, while the library is loaded. */
export function localInfo(videoId: string): LocalTrack | null {
  if (!isLocal(videoId)) return null;
  const id = videoId.slice(PREFIX.length);
  return useLocalLibrary.getState().tracks.find((t) => t.id === id) ?? null;
}

/** A local file as a queue track. */
export function toTrack(t: LocalTrack): Track {
  const artist = t.artist ?? t.albumArtist;
  return {
    videoId: `${PREFIX}${t.id}`,
    title: t.title,
    // Windows joins several artists with "; ".
    artists: artist ? artist.split(/\s*;\s*/).filter(Boolean).map((name) => ({ name, id: null })) : [],
    album: t.album ? { name: t.album, id: null } : null,
    durationSec: t.durationSec != null ? Math.round(t.durationSec) : null,
    explicit: false,
    // The picture embedded in the file, else a cover image beside it; 404 (placeholder) when neither.
    thumbnail: `/local-art/${t.id}`,
    index: t.track,
    plays: null,
  };
}

/** Album, then track number, then title: how a music folder is usually read. */
export function sortTracks(tracks: LocalTrack[]): LocalTrack[] {
  const text = (s: string | null) => (s ?? '').toLocaleLowerCase();
  return [...tracks].sort(
    (a, b) =>
      text(a.albumArtist ?? a.artist).localeCompare(text(b.albumArtist ?? b.artist)) ||
      text(a.album).localeCompare(text(b.album)) ||
      (a.track ?? 9999) - (b.track ?? 9999) ||
      text(a.title).localeCompare(text(b.title)),
  );
}

interface LocalLibraryState {
  /** False until the app first answers. */
  loaded: boolean;
  scanning: boolean;
  folders: string[];
  tracks: LocalTrack[];
  error: string | null;
}

export const useLocalLibrary = create<LocalLibraryState>()(() => ({
  loaded: false,
  scanning: false,
  folders: [],
  tracks: [],
  error: null,
}));

const send = (message: object) => webview?.postMessage(message);

export const localLibrary = {
  /** Asks the app for what it has; the answer lands in useLocalLibrary. */
  refresh: () => send({ local: 'list' }),
  /** Opens the app's folder picker; the new folder is scanned. */
  addFolder: () => send({ local: 'add' }),
  removeFolder: (folder: string) => send({ local: 'remove', folder }),
  rescan: () => send({ local: 'rescan' }),
};

webview?.addEventListener('message', (e) => {
  const m = e.data as { local?: string; scanning?: unknown; folders?: unknown; tracks?: unknown; message?: unknown } | null;
  if (m?.local === 'library' && Array.isArray(m.folders) && Array.isArray(m.tracks)) {
    useLocalLibrary.setState({
      loaded: true,
      scanning: m.scanning === true,
      folders: m.folders as string[],
      tracks: sortTracks(m.tracks as LocalTrack[]),
      error: null,
    });
  } else if (m?.local === 'error') {
    useLocalLibrary.setState({ error: typeof m.message === 'string' ? m.message : 'Scan failed' });
  }
});

// Loaded with the app, not the page: a local track's format (stats for nerds) is read from here.
localLibrary.refresh();
