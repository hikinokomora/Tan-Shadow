# Подгонка звуков лета: обрезка тишины, затухания, выравнивание громкости, бесшовные петли.
# Источники — оригиналы в Originals/, результат — WAV 48 кГц 16 бит в Assets/_Game/Audio (точная длина,
# без задержки кодера mp3, которая ломает петли и сдвигает удар).
# Запуск из корня репозитория: python ArtSource/Audio/Summer/process_summer_audio.py  (нужны ffmpeg и numpy).
# Числа в CLIPS подобраны по огибающим клипов; accent из отчёта переносится в ZhuqueSoundBank (SoundSlot.accent).
import subprocess, sys, os, json, wave
import numpy as np

SR = 48000
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "Originals")
DST = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "..", "Assets", "_Game", "Audio")

def load(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "2", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()

def save(path, x):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    pcm = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())

def windows(x, sec):
    m = x.mean(axis=1); w = int(sec * SR); n = max(1, len(m) // w)
    return np.sqrt(np.mean(m[:n * w].reshape(n, w) ** 2, axis=1) + 1e-12)

def db(v): return 20 * np.log10(v)

# name, source, folder, start, end, fadeIn, fadeOut, target dB, mode
# Уровни (дБ полной шкалы): финал −19; крик, потеря управления, огонь, удар о землю −21…−22; кувырок птицы −25;
# когти, взмахи, кувырок героини −26…−27; бульканье −28; шаги −31; петли (средняя громкость) −31…−33.
# Взмахи и шаги частые — тише разовых акцентов; фон — ниже всего, чтобы не забивать сигналы.
# mode "accent": громкость по самому громкому окну 100 мс (одиночные звуки);
# "loop": по средней громкости, конец бесшовно сводится с началом (crossfade = fadeIn).
CLIPS = [
    ("SFX_Zhuque_Cry",         "BirdsSound",            "SFX/Creatures/Zhuque", 0.36, 2.30, 0.01, 0.30, -21, "accent"),
    ("SFX_Zhuque_Wings",       "BirdsWingSwinging",     "SFX/Creatures/Zhuque", 0.12, 0.52, 0.01, 0.05, -27, "accent"),
    ("SFX_Zhuque_ClawsOnRock", "NailsNoiseRock",        "SFX/Creatures/Zhuque", 0.12, 0.55, 0.01, 0.04, -26, "accent"),
    ("SFX_Zhuque_LoseControl", "BirdsLoseControl",      "SFX/Creatures/Zhuque", 5.45, 6.91, 0.25, 0.06, -22, "accent"),
    ("SFX_Zhuque_FallOnSand",  "BirdsFallingOnSand",    "SFX/Creatures/Zhuque", 0.04, 0.45, 0.005, 0.15, -22, "accent"),
    ("SFX_Zhuque_TumbleOnSand","BirdsSomersaultOnSand", "SFX/Creatures/Zhuque", 0.36, 1.20, 0.01, 0.15, -25, "accent"),
    ("SFX_Zhuque_Burning",     "Burning",               "SFX/Creatures/Zhuque", 0.20, 1.90, 0.02, 0.30, -22, "accent"),
    ("SFX_Zhuque_FallIntoLava","FallingAndBurning",     "SFX/Creatures/Zhuque", 0.16, 1.67, 0.02, 0.10, -19,  "accent"),
    ("SFX_Lava_Gurgle",        "lava_gurgling",         "SFX/World",            0.10, 1.30, 0.01, 0.15, -28, "accent"),
    ("SFX_Player_StepSand",    "StepOnSand",            "SFX/Player",           0.04, 0.40, 0.005, 0.10, -31, "accent"),
    ("SFX_Player_RollSand",    "SomersaultOnSand",      "SFX/Player",           0.90, 1.60, 0.08, 0.15, -27, "accent"),
    ("AMB_Summer_LavaRiver",   "RiverOfTheLava",        "Ambience/Summer",      0.30, 5.50, 0.80, 0.0,  -33, "loop"),
    ("AMB_Summer_FlightWind",  "SoundOfFlying",         "Ambience/Summer",      0.15, 22.2, 1.00, 0.0,  -31, "loop"),
]

report = {}
for name, src, folder, a, b, fin, fout, target, mode in CLIPS:
    x = load(os.path.join(SRC, src + ".mp3"))
    x = x[int(a * SR):int(min(b, len(x) / SR) * SR)]
    if mode == "loop":
        c = int(fin * SR)
        body = x[:len(x) - c].copy()
        t = np.linspace(0, 1, c)[:, None]
        # Начало петли = начало, сведённое с хвостом: последний сэмпл переходит в первый без щелчка.
        body[:c] = x[:c] * np.sqrt(t) + x[len(x) - c:] * np.sqrt(1 - t)
        x = body
        level = db(np.sqrt(np.mean(x.mean(axis=1) ** 2)))
    else:
        n_in, n_out = max(1, int(fin * SR)), max(1, int(fout * SR))
        x[:n_in] *= np.linspace(0, 1, n_in)[:, None]
        x[-n_out:] *= np.linspace(1, 0, n_out)[:, None]
        level = db(windows(x, 0.1).max())
    x = x * 10 ** ((target - level) / 20)
    x = 0.95 * np.tanh(x / 0.95)          # мягкий ограничитель пиков
    out = os.path.join(DST, folder, name + ".wav")
    save(out, x)
    w = windows(x, 0.02)
    accent = int(np.argmax(w)) * 0.02 if mode == "accent" else 0.0
    report[name] = dict(length=round(len(x) / SR, 3), accent=round(accent, 2), peak_db=round(float(db(np.abs(x).max())), 1))
print(json.dumps(report, indent=1))
