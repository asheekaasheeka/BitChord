/**
 * The phone's "spatial audio" (app/.../playback/SpatialAudioProcessor.kt): widen the mid/side image,
 * then mix in a short, dulled cross-feed from the opposite channel, as Web Audio nodes.
 *
 * Per channel: out = OUTPUT * (same * own + cross * other + CROSSFEED * lowpass(delay(other))).
 */

/** How much wider the side signal gets; 1 is untouched. */
const WIDTH = 2.5;
/** Make-up after widening, so the wider side energy doesn't clip. */
const OUTPUT = 0.82;
const CROSSFEED = 0.2;
const DELAY_SEC = 0.015;
/** The phone's one-pole lowpass (coefficient 0.3 at 48 kHz) as a cutoff, so it holds at any sample rate. */
const LOWPASS_HZ = (-Math.log(1 - 0.3) * 48_000) / (2 * Math.PI);

/** The three gains of one output channel. Off is the identity: own channel only. */
export function spatialGains(on: boolean): { same: number; cross: number; feed: number } {
  if (!on) return { same: 1, cross: 0, feed: 0 };
  // mid + side·W = L·(1+W)/2 + R·(1−W)/2
  return { same: (OUTPUT * (1 + WIDTH)) / 2, cross: (OUTPUT * (1 - WIDTH)) / 2, feed: OUTPUT * CROSSFEED };
}

export class SpatialStage {
  readonly input: GainNode;
  readonly output: ChannelMergerNode;
  private readonly gains: { same: GainNode; cross: GainNode; feed: GainNode }[];

  constructor(private readonly ctx: BaseAudioContext) {
    // 'speakers' upmixes a mono source to both channels; a splitter on its own would leave R silent.
    this.input = new GainNode(ctx, { channelCount: 2, channelCountMode: 'explicit', channelInterpretation: 'speakers' });
    const split = new ChannelSplitterNode(ctx, { numberOfOutputs: 2 });
    this.output = new ChannelMergerNode(ctx, { numberOfInputs: 2 });
    this.input.connect(split);
    const alpha = 1 - Math.exp((-2 * Math.PI * LOWPASS_HZ) / ctx.sampleRate);
    const off = spatialGains(false);
    this.gains = [0, 1].map((ch) => {
      const other = 1 - ch;
      const same = new GainNode(ctx, { gain: off.same });
      const cross = new GainNode(ctx, { gain: off.cross });
      const feed = new GainNode(ctx, { gain: off.feed });
      split.connect(same, ch);
      split.connect(cross, other);
      split
        .connect(new DelayNode(ctx, { delayTime: DELAY_SEC, maxDelayTime: DELAY_SEC }), other)
        .connect(new IIRFilterNode(ctx, { feedforward: [alpha], feedback: [1, alpha - 1] }))
        .connect(feed);
      for (const node of [same, cross, feed]) node.connect(this.output, 0, ch);
      return { same, cross, feed };
    });
  }

  set(on: boolean): void {
    const now = this.ctx.currentTime;
    const target = spatialGains(on);
    for (const g of this.gains) {
      g.same.gain.setTargetAtTime(target.same, now, 0.05);
      g.cross.gain.setTargetAtTime(target.cross, now, 0.05);
      g.feed.gain.setTargetAtTime(target.feed, now, 0.05);
    }
  }
}
