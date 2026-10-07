"""Writes the default scheme's synthesized earcons into assets/sounds/default.

Usage:
    python tools/sounds/generate_earcons.py assets/sounds/default

Each cue is a few short sine notes with a smooth envelope (44.1 kHz, 16-bit mono), quiet enough
to sit under speech. The mode, boundary, wrap and error cues predate this script and are kept as
they are.
"""
import math
import struct
import sys
import wave

RATE = 44100


def notes(*parts, gain=0.25):
    """parts: (frequency Hz or 0 for silence, milliseconds)."""
    samples = []
    for frequency, ms in parts:
        count = int(RATE * ms / 1000)
        for i in range(count):
            # 5 ms fade in and out, so notes don't click
            fade = min(1.0, i / (RATE * 0.005), (count - i) / (RATE * 0.005))
            value = math.sin(2 * math.pi * frequency * i / RATE) if frequency else 0.0
            samples.append(int(32767 * gain * fade * value))
    return samples


CUES = {
    "list_entry": notes((660, 40), (0, 15), (880, 50)),
    "list_exit": notes((880, 40), (0, 15), (660, 50)),
    "table_entry": notes((523, 35), (0, 10), (659, 35), (0, 10), (784, 45)),
    "table_exit": notes((784, 35), (0, 10), (659, 35), (0, 10), (523, 45)),
    "landmark": notes((392, 70), (0, 10), (587, 90), gain=0.2),
    "clickable": notes((1568, 25), (0, 15), (1568, 25), gain=0.18),
    "progress": notes((1000, 30), gain=0.15),
}


def main(directory):
    for name, samples in CUES.items():
        with wave.open(f"{directory}/{name}.wav", "wb") as f:
            f.setnchannels(1)
            f.setsampwidth(2)
            f.setframerate(RATE)
            f.writeframes(struct.pack(f"<{len(samples)}h", *samples))


if __name__ == "__main__":
    main(sys.argv[1])
