#!/usr/bin/env python3
"""Generate every audio asset Isolith ships with.

Isolith contains no third-party art or audio. The sound effects are synthesised
here from plain maths — sine partials, an exponential envelope, and seeded
noise — and written as 16-bit mono PCM WAV files into ``assets/audio/``.

Only the Python standard library is used, so this runs on a clean checkout with
no ``pip install`` step:

    python3 tools/generate_assets.py

Output is deterministic: the noise generator is seeded per clip, so re-running
this script reproduces byte-identical files and never shows up as a spurious
diff. See ASSETS.md for the provenance record.
"""

from __future__ import annotations

import argparse
import math
import pathlib
import random
import struct
import wave

SAMPLE_RATE = 44_100
AMPLITUDE = 0.62  # headroom below clipping, before per-clip gain


# ---------------------------------------------------------------------------
# Envelope and oscillator helpers
# ---------------------------------------------------------------------------


def envelope(t: float, duration: float, attack: float, decay_shape: float) -> float:
    """Percussive envelope: a short linear attack, then exponential decay.

    ``decay_shape`` controls how abruptly the tail falls — higher is snappier.
    """
    if t < attack:
        return t / attack if attack > 0 else 1.0

    remaining = (t - attack) / max(duration - attack, 1e-6)
    return math.exp(-decay_shape * remaining)


def sine(frequency: float, t: float) -> float:
    return math.sin(math.tau * frequency * t)


def sweep(start_hz: float, end_hz: float, t: float, duration: float) -> float:
    """A sine whose frequency glides from ``start_hz`` to ``end_hz``.

    The phase is the integral of the (linearly interpolated) frequency, which is
    what keeps the glide continuous instead of clicking at each sample.
    """
    progress = min(t / duration, 1.0)
    mean_frequency = start_hz + (end_hz - start_hz) * progress * 0.5
    return math.sin(math.tau * mean_frequency * t)


def noise(rng: random.Random) -> float:
    return rng.uniform(-1.0, 1.0)


# ---------------------------------------------------------------------------
# Clips
# ---------------------------------------------------------------------------


def jump(rng: random.Random) -> list[float]:
    """A short upward blip — the classic platformer 'boing', kept dry."""
    duration = 0.18
    samples = []

    for i in range(int(SAMPLE_RATE * duration)):
        t = i / SAMPLE_RATE
        body = sweep(320.0, 660.0, t, duration)
        harmonic = 0.25 * sweep(640.0, 1320.0, t, duration)
        samples.append((body + harmonic) * envelope(t, duration, 0.004, 5.0) * 0.8)

    return samples


def land(rng: random.Random) -> list[float]:
    """A low thud: a filtered noise burst over a falling sine."""
    duration = 0.16
    samples = []
    previous = 0.0

    for i in range(int(SAMPLE_RATE * duration)):
        t = i / SAMPLE_RATE

        # One-pole low-pass over white noise gives a soft dust-impact texture
        # rather than a hiss.
        previous = previous * 0.86 + noise(rng) * 0.14
        thump = sweep(180.0, 70.0, t, duration)

        samples.append((thump * 0.9 + previous * 1.6) * envelope(t, duration, 0.002, 7.0))

    return samples


def collect(rng: random.Random) -> list[float]:
    """A two-note chime, a perfect fifth apart, for picking up a shard."""
    duration = 0.34
    notes = [(880.0, 0.0), (1318.5, 0.09)]  # A5 then E6
    samples = [0.0] * int(SAMPLE_RATE * duration)

    for frequency, offset in notes:
        for i in range(len(samples)):
            t = i / SAMPLE_RATE
            if t < offset:
                continue

            local = t - offset
            tone = sine(frequency, local) + 0.3 * sine(frequency * 2.0, local)
            samples[i] += tone * envelope(local, duration - offset, 0.003, 5.5) * 0.45

    return samples


def bounce(rng: random.Random) -> list[float]:
    """A springy launch: a fast rising sweep with a wobble on top."""
    duration = 0.28
    samples = []

    for i in range(int(SAMPLE_RATE * duration)):
        t = i / SAMPLE_RATE
        wobble = 1.0 + 0.12 * sine(18.0, t)
        body = sweep(220.0 * wobble, 900.0 * wobble, t, duration)
        samples.append(body * envelope(t, duration, 0.004, 4.0) * 0.85)

    return samples


def death(rng: random.Random) -> list[float]:
    """A falling tone — unmistakably a failure, but brief enough to retry into."""
    duration = 0.40
    samples = []

    for i in range(int(SAMPLE_RATE * duration)):
        t = i / SAMPLE_RATE
        body = sweep(520.0, 130.0, t, duration)
        detune = 0.35 * sweep(516.0, 128.0, t, duration)  # slight beating
        samples.append((body + detune) * envelope(t, duration, 0.005, 3.4) * 0.7)

    return samples


def complete(rng: random.Random) -> list[float]:
    """A rising four-note arpeggio for finishing a course."""
    duration = 0.95
    # A major triad plus the octave: A4 C#5 E5 A5.
    notes = [(440.0, 0.00), (554.4, 0.11), (659.3, 0.22), (880.0, 0.33)]
    samples = [0.0] * int(SAMPLE_RATE * duration)

    for frequency, offset in notes:
        for i in range(len(samples)):
            t = i / SAMPLE_RATE
            if t < offset:
                continue

            local = t - offset
            tone = sine(frequency, local) + 0.28 * sine(frequency * 2.0, local)
            samples[i] += tone * envelope(local, duration - offset, 0.004, 3.0) * 0.34

    return samples


CLIPS = {
    "jump": jump,
    "land": land,
    "collect": collect,
    "bounce": bounce,
    "death": death,
    "complete": complete,
}


# ---------------------------------------------------------------------------
# Writing
# ---------------------------------------------------------------------------


def normalise(samples: list[float]) -> list[float]:
    """Scale to a consistent peak so no clip is dramatically louder than another."""
    peak = max((abs(value) for value in samples), default=0.0)
    if peak <= 1e-9:
        return samples

    gain = AMPLITUDE / peak
    return [value * gain for value in samples]


def fade_out(samples: list[float], milliseconds: float = 4.0) -> list[float]:
    """Taper the tail to zero so the file cannot end on a click."""
    count = min(len(samples), int(SAMPLE_RATE * milliseconds / 1000.0))
    if count == 0:
        return samples

    for offset in range(count):
        index = len(samples) - count + offset
        samples[index] *= 1.0 - (offset / count)

    return samples


def write_wav(path: pathlib.Path, samples: list[float]) -> None:
    frames = b"".join(
        struct.pack("<h", int(max(-1.0, min(1.0, value)) * 32767))
        for value in samples
    )

    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        handle.writeframes(frames)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--out",
        type=pathlib.Path,
        default=pathlib.Path(__file__).resolve().parent.parent / "assets" / "audio",
        help="directory to write the WAV files into",
    )
    arguments = parser.parse_args()

    arguments.out.mkdir(parents=True, exist_ok=True)

    for name, build in CLIPS.items():
        # Seeded per clip name: the same clip is identical on every machine and
        # every run, so regenerating never dirties the working tree.
        rng = random.Random(f"isolith:{name}")
        samples = fade_out(normalise(build(rng)))

        path = arguments.out / f"{name}.wav"
        write_wav(path, samples)
        print(f"wrote {path.relative_to(pathlib.Path.cwd())} ({len(samples)} samples)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
