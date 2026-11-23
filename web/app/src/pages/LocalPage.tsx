import { FolderPlus, RefreshCw, Shuffle, X, Play } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { PageHeader } from '@/components/PageHeader';
import { playList, shuffleList } from '@/components/music/play';
import { EmptyState, stateStyles } from '@/components/music/States';
import { TrackList } from '@/components/music/TrackList';
import { localLibrary, localSupported, toTrack, useLocalLibrary } from '@/data/local/library';
import { settingsUi } from './settingsStyles';

const button =
  'inline-flex h-8 items-center gap-1.5 rounded-pill bg-quaternary px-3.5 type-body-emphasized text-primary hover:brightness-125 disabled:opacity-50';
const keyButton = `${button} bg-key-bg text-white`;

/** Local Files (Windows app only): the folders the user added, and every song in them. */
export default function LocalPage() {
  const { loaded, scanning, folders, tracks, error } = useLocalLibrary();
  const [filter, setFilter] = useState('');
  useEffect(() => localLibrary.refresh(), []);

  const shown = useMemo(() => {
    const q = filter.trim().toLocaleLowerCase();
    const list = q
      ? tracks.filter((t) => [t.title, t.artist, t.album, t.albumArtist].some((s) => s?.toLocaleLowerCase().includes(q)))
      : tracks;
    return list.map(toTrack);
  }, [tracks, filter]);

  if (!localSupported) {
    return <EmptyState title="Local files need the Windows app" detail="Only the BitChord app for Windows can read music folders on this PC." />;
  }

  const context = { source: { title: 'Local Files', type: 'library' as const, id: 'local' } };
  return (
    <>
      <PageHeader title="Local Files" />
      <div className="mx-(--body-gutter) mt-4 flex flex-wrap items-center gap-2">
        <button className={keyButton} disabled={!shown.length} onClick={() => playList(shown, 0, context)}>
          <Play size={15} fill="currentColor" /> Play
        </button>
        <button className={button} disabled={!shown.length} onClick={() => shuffleList(shown, context)}>
          <Shuffle size={15} /> Shuffle
        </button>
        <button className={button} disabled={scanning} onClick={localLibrary.addFolder}>
          <FolderPlus size={15} /> Add Folder
        </button>
        <button className={button} disabled={scanning || !folders.length} onClick={localLibrary.rescan}>
          <RefreshCw size={15} className={scanning ? 'animate-spin' : ''} /> {scanning ? 'Scanning…' : 'Rescan'}
        </button>
        {tracks.length > 0 && (
          <input
            className={`${settingsUi.input} ms-auto`}
            type="search"
            placeholder="Filter"
            aria-label="Filter local songs"
            value={filter}
            onChange={(e) => setFilter(e.currentTarget.value)}
          />
        )}
      </div>

      {folders.length > 0 && (
        <section className={`${settingsUi.group} mx-(--body-gutter) mt-4 max-w-[640px]`}>
          {folders.map((folder) => (
            <div key={folder} className={settingsUi.row}>
              <span className="min-w-0 truncate" title={folder}>
                {folder}
              </span>
              <button
                className="grid size-7 flex-none place-items-center rounded-full text-secondary hover:bg-quaternary hover:text-primary disabled:opacity-50"
                aria-label={`Remove ${folder}`}
                disabled={scanning}
                onClick={() => localLibrary.removeFolder(folder)}
              >
                <X size={15} />
              </button>
            </div>
          ))}
        </section>
      )}
      {error && <p className={`${stateStyles.detail} mx-(--body-gutter) mt-3`}>Scan failed: {error}</p>}

      {!loaded ? null : !folders.length ? (
        <EmptyState title="No music folders yet" detail="Add a folder and BitChord plays the MP3, FLAC, M4A, OGG, Opus and WAV files in it." />
      ) : !tracks.length && !scanning ? (
        <EmptyState title="No songs found" detail="These folders hold no audio files BitChord can play." />
      ) : (
        <div className="mt-6">
          <p className={`${stateStyles.detail} mx-(--body-gutter) mb-2`}>
            {shown.length === tracks.length ? `${tracks.length} songs` : `${shown.length} of ${tracks.length} songs`}
          </p>
          <TrackList tracks={shown} variant="playlist" source={context.source} />
        </div>
      )}
    </>
  );
}
