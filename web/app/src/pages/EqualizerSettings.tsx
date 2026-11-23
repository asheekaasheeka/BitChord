import { Select } from '@/components/Select';
import { EQ_BANDS_HZ, EQ_PRESETS, EQ_RANGE_DB, matchingPreset, type EqPreset } from '@/playback/equalizer';
import { usePlayer } from '@/state/player';
import { Toggle } from './IntegrationSettings';
import { settingsUi } from './settingsStyles';

const hzLabel = (hz: number) => (hz >= 1000 ? `${hz / 1000} kHz` : `${hz} Hz`);
const dbLabel = (db: number) => `${db > 0 ? '+' : ''}${db.toFixed(1)} dB`;

/** Settings' Equalizer group: on/off, a preset, and the seven sliders (the phone's manual tab). */
export function EqualizerSettings() {
  const on = usePlayer((s) => s.eqOn);
  const bands = usePlayer((s) => s.eqBands);
  const { setEq } = usePlayer.getState();
  const preset = matchingPreset(bands);
  const presets = Object.keys(EQ_PRESETS) as EqPreset[];

  return (
    <section className={settingsUi.group}>
      <Toggle label="Equalizer" checked={on} onChange={(next) => setEq(next, bands)} />
      <label className={settingsUi.controlRow}>
        <span>Preset</span>
        <Select<string>
          label="Preset"
          value={preset}
          options={[
            ...presets.map((name) => ({ value: name, label: name })),
            ...(preset === 'Custom' ? [{ value: 'Custom', label: 'Custom' }] : []),
          ]}
          onChange={(name) => name !== 'Custom' && setEq(true, [...EQ_PRESETS[name as EqPreset]])}
        />
      </label>
      {EQ_BANDS_HZ.map((hz, i) => (
        <label key={hz} className={`${settingsUi.row} gap-4 ${on ? '' : 'opacity-50'}`}>
          <span className="w-16 flex-none">{hzLabel(hz)}</span>
          <input
            type="range"
            className="min-w-0 flex-1 cursor-pointer accent-green disabled:cursor-default"
            min={-EQ_RANGE_DB}
            max={EQ_RANGE_DB}
            step={0.5}
            value={bands[i]}
            disabled={!on}
            aria-valuetext={dbLabel(bands[i]!)}
            onChange={(e) => setEq(true, bands.map((db, j) => (j === i ? Number(e.currentTarget.value) : db)))}
            onDoubleClick={() => setEq(true, bands.map((db, j) => (j === i ? 0 : db)))}
          />
          <span className={`${settingsUi.value} w-16 flex-none justify-end tabular-nums`}>{dbLabel(bands[i]!)}</span>
        </label>
      ))}
    </section>
  );
}
