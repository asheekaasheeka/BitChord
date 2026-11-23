import { Moon } from 'lucide-react';
import { useState } from 'react';
import { Menu, MenuHeader, MenuItem, MenuSeparator } from '@/components/Menu';
import { usePlayer } from '@/state/player';

/** The phone's sleep timer presets, in minutes. */
const PRESETS = [15, 30, 45, 60, 90, 120];

function remainingText(at: number): string {
  const min = Math.max(1, Math.ceil((at - performance.now()) / 60_000));
  return min >= 60 ? `${Math.floor(min / 60)} h ${min % 60} min` : `${min} min`;
}

/** Player bar: pause after a while, or at the end of this track. Lit while armed. */
export function SleepTimerButton({ className }: { className: string }) {
  const sleepAt = usePlayer((s) => s.sleepAt);
  const afterTrack = usePlayer((s) => s.sleepAfterTrack);
  const { setSleepTimer } = usePlayer.getState();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const armed = sleepAt !== null || afterTrack;
  const close = () => setAnchor(null);
  const pick = (when: number | 'track' | null) => {
    setSleepTimer(when);
    close();
  };

  return (
    <>
      <button
        className={className}
        aria-label="Sleep Timer"
        aria-pressed={armed}
        aria-haspopup="menu"
        aria-expanded={anchor !== null}
        onClick={(e) => setAnchor(anchor ? null : e.currentTarget)}
      >
        <Moon size={17} />
      </button>
      {anchor && (
        <Menu anchor={anchor} placement="above-end" onClose={close} label="Sleep Timer">
          <MenuHeader>
            {sleepAt !== null ? `Pauses in ${remainingText(sleepAt)}` : afterTrack ? 'Pauses after this track' : 'Sleep Timer'}
          </MenuHeader>
          {PRESETS.map((min) => (
            <MenuItem key={min} onSelect={() => pick(min)}>
              {min >= 60 ? `${min / 60} hour${min > 60 ? 's' : ''}` : `${min} minutes`}
            </MenuItem>
          ))}
          <MenuItem onSelect={() => pick('track')}>
            End of Track
          </MenuItem>
          {armed && (
            <>
              <MenuSeparator />
              <MenuItem destructive onSelect={() => pick(null)}>
                Turn Off Timer
              </MenuItem>
            </>
          )}
        </Menu>
      )}
    </>
  );
}
