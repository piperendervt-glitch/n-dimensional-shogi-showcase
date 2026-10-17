"""Synthesizes the 4D shogi game's sounds (no external samples, so no licence questions).

    python tools/synth_audio.py

Writes Unity/Assets/Showcase/Settings/Resources/Cfg2Audio/bgm.wav (a seamless calm loop),
piece.wav (the click of a piece on a wooden board), menu-select.wav and
menu-confirm.wav (soft UI sounds). The output is deterministic.
"""
import os
import wave

import numpy as np

RATE = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "Unity", "Assets", "Showcase", "Settings", "Resources", "Cfg2Audio")


def write_wav(path, samples, channels=1):
    data = np.clip(samples, -1, 1)
    pcm = (data * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def note(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def bgm():
    # 48 s loop: four 12 s chords of a D yo-scale pad over a low drone, with sparse soft plucks.
    length = 48 * RATE
    cycles = 3  # events repeat every cycle; the middle cycle is the steady-state loop including reverb tails
    total = length * cycles
    t = np.arange(total) / RATE
    out = np.zeros((total, 2))

    # Drone D2 + A2 with a very slow swell (periods divide the loop so it wraps cleanly).
    # Drone frequencies are rounded to whole cycles per loop so the drone has no phase jump at the seam.
    def whole(f):
        return round(f * 48) / 48
    swell = 0.75 + 0.25 * np.sin(2 * np.pi * t / 24)
    drone = (np.sin(2 * np.pi * whole(note(38)) * t) + 0.6 * np.sin(2 * np.pi * whole(note(45)) * t) + 0.15 * np.sin(2 * np.pi * whole(note(50)) * t)) * 0.10 * swell
    out += drone[:, None]

    chords = [[62, 64, 69, 74], [59, 64, 67, 71], [55, 62, 67, 69], [57, 62, 64, 69]]
    chord_len = 12 * RATE
    fade = 4 * RATE
    for c in range(cycles * 4):
        start = c * chord_len - fade // 2
        seg = chord_len + fade
        tt = np.arange(seg) / RATE
        env = np.ones(seg)
        env[:fade] = np.sin(np.linspace(0, np.pi / 2, fade)) ** 2
        env[-fade:] = np.cos(np.linspace(0, np.pi / 2, fade)) ** 2
        left = np.zeros(seg)
        right = np.zeros(seg)
        for i, m in enumerate(chords[c % 4]):
            f = note(m)
            # Two slightly detuned partials per voice give a slow chorus; soft 2nd harmonic for warmth.
            v = np.sin(2 * np.pi * f * tt + i) + 0.2 * np.sin(2 * np.pi * 2 * f * tt)
            w = np.sin(2 * np.pi * f * 1.003 * tt + 2 * i) + 0.2 * np.sin(2 * np.pi * 2 * f * 1.003 * tt)
            pan = 0.35 + 0.1 * i
            left += v * (1 - pan) + w * pan * 0.6
            right += v * pan * 0.6 + w * (1 - pan)
        lo, hi = max(start, 0), min(start + seg, total)
        out[lo:hi, 0] += (left * env * 0.035)[lo - start:hi - start]
        out[lo:hi, 1] += (right * env * 0.035)[lo - start:hi - start]

    # Sparse koto-like plucks from the scale, the same pattern each cycle.
    rng = np.random.default_rng(4)
    scale = [74, 76, 79, 81, 83, 86, 88]
    pattern = []
    pos = 1.5
    while pos < 46:
        pattern.append((pos, scale[rng.integers(len(scale))], rng.uniform(0.5, 1.0), rng.uniform(0.25, 0.75)))
        pos += rng.choice([2.0, 3.0, 3.0, 4.0, 6.0])
    pluck_len = int(3.5 * RATE)
    tp = np.arange(pluck_len) / RATE
    for c in range(cycles):
        for when, m, vel, pan in pattern:
            f = note(m)
            tone = (np.sin(2 * np.pi * f * tp) * np.exp(-tp * 2.2) + 0.35 * np.sin(2 * np.pi * 2 * f * tp) * np.exp(-tp * 5)
                    + 0.12 * np.sin(2 * np.pi * 3 * f * tp) * np.exp(-tp * 9))
            tone *= np.minimum(1, tp / 0.004) * vel * 0.06
            s = c * length + int(when * RATE)
            e = min(s + pluck_len, total)
            out[s:e, 0] += tone[:e - s] * (1 - pan)
            out[s:e, 1] += tone[:e - s] * pan

    # Gentle feedback-delay reverb (one pass over all cycles; the middle cycle sees the tails of the first).
    wet = np.zeros_like(out)
    for delay, gain in [(0.37, 0.35), (0.53, 0.28), (0.79, 0.22)]:
        d = int(delay * RATE)
        buf = out.copy()
        for k in range(1, 6):
            shift = d * k
            wet[shift:] += buf[:-shift] * gain ** k
    mix = out + wet * 0.6

    loop = mix[length:2 * length]
    # Soft low-pass (one-pole) to keep it calm; run over the previous cycle first so the filter state wraps.
    a = 0.25
    prev = mix[length - 2000:length].copy()
    state = np.zeros(2)
    for x in prev:
        state += a * (x - state)
    filt = np.empty_like(loop)
    for i in range(len(loop)):
        state += a * (loop[i] - state)
        filt[i] = state
    filt /= np.max(np.abs(filt)) / 0.5  # peak -6 dBFS
    return filt


def piece():
    # A shogi piece set down on a kaya board: a bright, dry "pachi". The hard edge of the piece gives a
    # short high click (2-6 kHz); the board answers with a brief mid-range body (~0.5-1.5 kHz).
    n = int(0.18 * RATE)
    t = np.arange(n) / RATE
    modes = [(480, 0.020, 0.25), (1050, 0.018, 0.45), (1730, 0.016, 0.75), (2480, 0.012, 0.9),
             (3350, 0.008, 0.7), (4600, 0.005, 0.45), (6100, 0.003, 0.25)]
    s = sum(a * np.sin(2 * np.pi * f * t + i) * np.exp(-t / d) for i, (f, d, a) in enumerate(modes))
    rng = np.random.default_rng(7)
    noise = rng.standard_normal(n) * np.exp(-t / 0.0012) * 0.6
    noise = np.convolve(noise, np.ones(2) / 2, mode="same")  # keep the crack, drop only the harshest hiss
    s = s + noise
    s *= np.minimum(1, t / 0.0003)
    s[-200:] *= np.linspace(1, 0, 200)
    return s / np.max(np.abs(s)) * 0.7


def menu_select():
    # A very soft high tick for moving the menu cursor.
    n = int(0.08 * RATE)
    t = np.arange(n) / RATE
    s = (np.sin(2 * np.pi * 1760 * t) + 0.25 * np.sin(2 * np.pi * 3520 * t)) * np.exp(-t / 0.014)
    s *= np.minimum(1, t / 0.0015)
    s[-100:] *= np.linspace(1, 0, 100)
    return s / np.max(np.abs(s)) * 0.3


def menu_confirm():
    # A gentle wooden marimba-like "pon" (fundamental plus its 4th partial) for pressing a button.
    n = int(0.30 * RATE)
    t = np.arange(n) / RATE
    f = note(81)
    s = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.075) + 0.3 * np.sin(2 * np.pi * 4 * f * t) * np.exp(-t / 0.02)
    s *= np.minimum(1, t / 0.002)
    s[-200:] *= np.linspace(1, 0, 200)
    return s / np.max(np.abs(s)) * 0.4


def main():
    os.makedirs(OUT, exist_ok=True)
    loop = bgm()
    write_wav(os.path.join(OUT, "bgm.wav"), loop.reshape(-1), channels=2)
    write_wav(os.path.join(OUT, "piece.wav"), piece())
    write_wav(os.path.join(OUT, "menu-select.wav"), menu_select())
    write_wav(os.path.join(OUT, "menu-confirm.wav"), menu_confirm())
    print("bgm", len(loop) / RATE, "s; seam step", float(np.max(np.abs(loop[0] - loop[-1]))))


if __name__ == "__main__":
    main()
