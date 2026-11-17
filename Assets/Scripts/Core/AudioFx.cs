using System.Collections.Generic;
using UnityEngine;

namespace DCT
{
    /// <summary>
    /// Procedurally synthesized audio: server-room hum that scales with load, an ambient music loop,
    /// and UI / event sound effects. No audio assets required.
    /// </summary>
    public class AudioFx : MonoBehaviour
    {
        public GameManager gm;
        AudioSource hum, music, sfx;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        int lastObjects = -1, lastTiles = -1;
        float lastPlay;
        const int Rate = 44100;

        void Start()
        {
            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = MakeHum();
            hum.loop = true;
            hum.volume = 0;
            hum.Play();

            music = gameObject.AddComponent<AudioSource>();
            music.clip = MakeMusic();
            music.loop = true;
            music.Play();

            sfx = gameObject.AddComponent<AudioSource>();
            clips["click"] = Tone(new[] { 1200f }, 0.04f, 0.25f, 30f);
            clips["build"] = Thud();
            clips["good"] = Tone(new[] { 660f, 880f, 1320f }, 0.35f, 0.25f, 6f, true);
            clips["warn"] = Tone(new[] { 520f, 390f }, 0.3f, 0.25f, 5f, true);
            clips["alarm"] = Alarm();
            clips["cash"] = Tone(new[] { 1568f, 2093f }, 0.25f, 0.2f, 9f, true);
        }

        public void Play(string name, float vol = 1f)
        {
            if (sfx == null || !clips.TryGetValue(name, out var c)) return;
            if (Time.unscaledTime - lastPlay < 0.05f) return;
            lastPlay = Time.unscaledTime;
            sfx.PlayOneShot(c, vol * Settings.Sfx * Settings.Master);
        }

        public void OnLog(int severity)
        {
            if (severity == 3) Play("alarm", 0.6f);
            else if (severity == 2) Play("warn", 0.5f);
            else if (severity == 1) Play("good", 0.35f);
        }

        void Update()
        {
            AudioListener.volume = 1f;
            if (music != null) music.volume = Settings.Music * Settings.Master * 0.5f;
            if (gm == null || !gm.inGame || gm.sim == null)
            {
                if (hum != null) hum.volume = Mathf.MoveTowards(hum.volume, 0, Time.unscaledDeltaTime);
                return;
            }
            var sim = gm.sim;
            // Hum gets louder (and slightly higher) with IT load and as you zoom in.
            float load = Mathf.Clamp01(sim.itLoadKW / 400f);
            float zoom = Mathf.InverseLerp(70f, 8f, gm.cam.distance);
            float target = (sim.itLoadKW > 0.1f ? 0.08f + 0.35f * load : 0f) * (0.4f + 0.6f * zoom) * Settings.Sfx * Settings.Master;
            hum.volume = Mathf.MoveTowards(hum.volume, target, Time.unscaledDeltaTime * 0.5f);
            hum.pitch = 0.9f + load * 0.25f;

            if (lastObjects >= 0 && sim.objectsVersion != lastObjects) Play("build", 0.7f);
            else if (lastTiles >= 0 && sim.tilesVersion != lastTiles) Play("build", 0.5f);
            lastObjects = sim.objectsVersion;
            lastTiles = sim.tilesVersion;
        }

        // ------------------------------------------------------------ synthesis
        static AudioClip Make(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip MakeHum()
        {
            int n = Rate * 4;
            var d = new float[n];
            var r = new System.Random(3);
            float lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)r.NextDouble() * 2 - 1;
                lp += (noise - lp) * 0.05f;      // fan whoosh
                lp2 += (lp - lp2) * 0.2f;
                float mains = Mathf.Sin(2 * Mathf.PI * 60f * t) * 0.35f + Mathf.Sin(2 * Mathf.PI * 120f * t) * 0.2f + Mathf.Sin(2 * Mathf.PI * 180f * t) * 0.06f;
                d[i] = (lp2 * 1.8f + mains * 0.4f) * 0.5f;
            }
            // Crossfade the loop seam.
            int fade = Rate / 5;
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; d[i] = d[i] * k + d[n - fade + i] * (1 - k); }
            System.Array.Resize(ref d, n - fade);
            return Make("hum", d);
        }

        static AudioClip MakeMusic()
        {
            // Slow ambient pad: four chords, 8 s each, soft sine/triangle voices with a gentle arpeggio.
            float[][] chords =
            {
                new[] { 220.00f, 261.63f, 329.63f, 392.00f },  // Am7
                new[] { 174.61f, 220.00f, 261.63f, 329.63f },  // Fmaj7
                new[] { 196.00f, 246.94f, 293.66f, 392.00f },  // G
                new[] { 164.81f, 207.65f, 246.94f, 329.63f },  // E
            };
            float chordLen = 8f;
            int n = (int)(Rate * chordLen * chords.Length);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                int ci = (int)(t / chordLen) % chords.Length;
                float local = t - ci * chordLen;
                float env = Mathf.Clamp01(local / 1.5f) * Mathf.Clamp01((chordLen - local) / 1.5f);
                float s = 0;
                foreach (var f in chords[ci])
                    s += Mathf.Sin(2 * Mathf.PI * f * 0.5f * t) * 0.18f + Mathf.Sin(2 * Mathf.PI * f * t + Mathf.Sin(t * 0.7f)) * 0.07f;
                // Arpeggio
                int step = (int)(local * 2f);
                float stepT = local * 2f - step;
                float af = chords[ci][step % 4] * 2f;
                float aenv = Mathf.Exp(-stepT * 5f) * 0.12f;
                s += Mathf.Sin(2 * Mathf.PI * af * t) * aenv;
                d[i] = s * env * 0.45f;
            }
            return Make("music", d);
        }

        static AudioClip Tone(float[] freqs, float len, float vol, float decay, bool sequence = false)
        {
            int n = (int)(Rate * len);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = 0;
                if (sequence)
                {
                    int idx = Mathf.Min(freqs.Length - 1, (int)(t / len * freqs.Length));
                    float lt = t - idx * len / freqs.Length;
                    s = Mathf.Sin(2 * Mathf.PI * freqs[idx] * t) * Mathf.Exp(-lt * decay);
                }
                else foreach (var f in freqs) s += Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * decay);
                d[i] = s * vol;
            }
            return Make("tone", d);
        }

        static AudioClip Thud()
        {
            int n = (int)(Rate * 0.25f);
            var d = new float[n];
            var r = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(140f, 50f, t * 4f);
                d[i] = (Mathf.Sin(2 * Mathf.PI * f * t) * 0.6f + ((float)r.NextDouble() * 2 - 1) * 0.15f * Mathf.Exp(-t * 40f)) * Mathf.Exp(-t * 14f) * 0.6f;
            }
            return Make("thud", d);
        }

        static AudioClip Alarm()
        {
            int n = (int)(Rate * 0.9f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = ((int)(t * 6f) % 2 == 0) ? 880f : 660f;
                float s = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t)) * 0.5f + Mathf.Sin(2 * Mathf.PI * f * t) * 0.5f;
                d[i] = s * 0.18f * Mathf.Clamp01((0.9f - t) * 5f);
            }
            return Make("alarm", d);
        }
    }
}
