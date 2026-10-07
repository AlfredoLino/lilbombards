using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Efectos de sonido sintetizados en tiempo de carga (sin archivos de audio).
    /// Para reemplazarlos por sonidos propios, basta con asignar otros AudioClip a estos campos.
    /// </summary>
    public static class Sfx
    {
        public static AudioClip Explosion, IceBlast, Punch, PunchStrong, Swish, Jump, Pickup, Throw, Powerup, Curse,
            Freeze, Shatter, Beep, Death, Fall, Sticky, Spawn, Pop, Bounce, ShieldBreak, Ding, Go, Win, Scream;

        const int SR = 44100;
        static AudioSource[] pool;
        static int next;
        static bool generated;
        static System.Random rng = new System.Random(1234);

        public static void Init(Transform host)
        {
            if (!generated)
            {
                generated = true;
                Generate();
            }
            pool = new AudioSource[24];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(host, false);
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false;
                a.spatialBlend = 0f;
                pool[i] = a;
            }
        }

        // Registro de clips para enviarlos por red como un numero.
        static readonly List<AudioClip> clipList = new List<AudioClip>();
        static readonly Dictionary<AudioClip, short> clipIds = new Dictionary<AudioClip, short>();

        static void RegisterClips()
        {
            clipList.Clear();
            clipIds.Clear();
            var all = new List<AudioClip> { Explosion, IceBlast, Punch, PunchStrong, Swish, Jump, Pickup, Throw, Powerup, Curse,
                Freeze, Shatter, Beep, Death, Fall, Sticky, Spawn, Pop, Bounce, ShieldBreak, Ding, Go, Win };
            if (Screams != null) foreach (var set in Screams) all.AddRange(set);
            if (Oofs != null) all.AddRange(Oofs);
            foreach (var c in all)
            {
                if (c == null || clipIds.ContainsKey(c)) continue;
                clipIds[c] = (short)clipList.Count;
                clipList.Add(c);
            }
        }

        public static AudioClip ClipById(short id) => id >= 0 && id < clipList.Count ? clipList[id] : null;

        public static AudioSource Play(AudioClip c, float vol = 1f, float pitch = 1f, float pan = 0f)
        {
            if (pool == null || c == null) return null;
            // Online: en el cliente solo suenan los sonidos que manda el anfitrion; el anfitrion los reenvia.
            if (!Net.FxAllowed) return null;
            if (Net.IsHost && clipIds.TryGetValue(c, out short cid)) Net.EvSound(cid, vol, pitch, pan);
            var a = pool[next];
            next = (next + 1) % pool.Length;
            if (a == null) return null;
            a.Stop();
            a.clip = c;
            a.volume = Mathf.Clamp01(vol);
            a.pitch = pitch;
            a.panStereo = pan;
            a.Play();
            return a;
        }

        /// <summary>Como PlayAt pero con un tono fijo (voz de cada personaje). Devuelve la fuente para poder cortarla.</summary>
        public static AudioSource PlayVoice(AudioClip c, Vector3 pos, float vol, float pitch)
        {
            float pan = 0f;
            if (CameraRig.I != null)
                pan = Mathf.Clamp((CameraRig.I.Cam.WorldToViewportPoint(pos).x - 0.5f) * 1.2f, -0.8f, 0.8f);
            return Play(c, vol, pitch, pan);
        }

        /// <summary>Desvanece y detiene un sonido en curso (solo si la fuente sigue sonando ese clip).</summary>
        public static System.Collections.IEnumerator FadeOut(AudioSource a, AudioClip clip, float time)
        {
            if (a == null) yield break;
            float v0 = a.volume;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                if (a == null || a.clip != clip || !a.isPlaying) yield break;
                a.volume = v0 * (1f - t / time);
                yield return null;
            }
            if (a != null && a.clip == clip) a.Stop();
        }

        public static void PlayAt(AudioClip c, Vector3 pos, float vol = 1f)
        {
            float pan = 0f;
            if (CameraRig.I != null)
            {
                var vp = CameraRig.I.Cam.WorldToViewportPoint(pos);
                pan = Mathf.Clamp((vp.x - 0.5f) * 1.2f, -0.8f, 0.8f);
            }
            Play(c, vol, Random.Range(0.94f, 1.06f), pan);
        }

        // ------------------------------------------------------------------ Sintesis

        static float White() => (float)(rng.NextDouble() * 2.0 - 1.0);

        static AudioClip Clip(string name, float[] d, float gain)
        {
            float peak = 0.0001f;
            foreach (var s in d) peak = Mathf.Max(peak, Mathf.Abs(s));
            float k = gain / peak;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
            var c = AudioClip.Create(name, d.Length, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        static float[] Buf(float seconds) => new float[Mathf.CeilToInt(seconds * SR)];

        static void Generate()
        {
            Explosion = Clip("explosion", Boom(1.6f, 60f, 0.08f, false), 0.95f);
            IceBlast = Clip("iceblast", Boom(1.2f, 90f, 0.15f, true), 0.85f);
            Punch = Clip("punch", Thump(0.18f, 170f, 60f, 25f, 0.5f), 0.8f);
            PunchStrong = Clip("punch2", Thump(0.32f, 130f, 40f, 12f, 0.9f), 0.95f);
            Bounce = Clip("bounce", Thump(0.09f, 140f, 90f, 40f, 0.2f), 0.5f);
            Swish = Clip("swish", Whoosh(0.16f, 0.25f, 0.6f), 0.35f);
            Throw = Clip("throw", Whoosh(0.24f, 0.08f, 0.5f), 0.5f);
            Jump = Clip("jump", Sweep(0.15f, 260f, 520f, 14f, 0.3f), 0.45f);
            Pickup = Clip("pickup", Notes(new[] { 700f, 1050f }, 0.05f, 30f), 0.4f);
            Powerup = Clip("powerup", Notes(new[] { 523f, 659f, 784f, 1047f, 1319f }, 0.07f, 9f), 0.55f);
            Ding = Clip("ding", Notes(new[] { 880f }, 0.4f, 7f), 0.55f);
            Go = Clip("go", Notes(new[] { 660f, 1320f }, 0.18f, 5f), 0.6f);
            Win = Clip("win", Notes(new[] { 523f, 659f, 784f, 1047f, 784f, 1047f }, 0.16f, 3f), 0.6f);
            Beep = Clip("beep", Sweep(0.09f, 1400f, 1400f, 20f, 0.6f), 0.4f);
            Spawn = Clip("spawn", Sweep(0.35f, 300f, 950f, 6f, 0.2f), 0.35f);
            Pop = Clip("pop", Sweep(0.12f, 520f, 180f, 25f, 0.1f), 0.5f);
            Curse = Clip("curse", Wail(0.9f, 320f, 140f, 7f, true), 0.6f);
            Death = Clip("death", Wail(0.7f, 420f, 170f, 9f, false), 0.6f);
            Fall = Clip("fall", Sweep(1.0f, 900f, 220f, 1.5f, 0f), 0.45f);
            Freeze = Clip("freeze", Shimmer(0.7f), 0.55f);
            Shatter = Clip("shatter", Crackle(0.55f), 0.8f);
            ShieldBreak = Clip("shieldbreak", Crackle(0.35f), 0.6f);
            Sticky = Clip("sticky", Squelch(0.16f), 0.6f);
            GenerateScreams();
            RegisterClips();
        }

        static float[] Boom(float len, float baseHz, float lpK, bool ice)
        {
            var d = Buf(len);
            float lp = 0f, lp2 = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float n = White();
                lp += (n - lp) * lpK;
                lp2 += (lp - lp2) * 0.25f;
                float f = baseHz * Mathf.Exp(-t * 1.5f) + 28f;
                phase += 2f * Mathf.PI * f / SR;
                float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 4f);
                float crack = n * Mathf.Exp(-t * 35f) * 0.7f;
                float env = t < 0.004f ? t / 0.004f : Mathf.Exp(-t * 3.2f);
                float s = (lp2 * 4f + boom * 0.9f + crack) * env;
                if (ice) s += (Mathf.Sin(t * 2f * Mathf.PI * 2600f) + Mathf.Sin(t * 2f * Mathf.PI * 3700f)) * Mathf.Exp(-t * 9f) * 0.15f;
                d[i] = s;
            }
            return d;
        }

        static float[] Thump(float len, float f0, float f1, float decay, float noise)
        {
            var d = Buf(len);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float f = Mathf.Lerp(f1, f0, Mathf.Exp(-t * 30f));
                phase += 2f * Mathf.PI * f / SR;
                lp += (White() - lp) * 0.3f;
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-t * decay) + lp * noise * Mathf.Exp(-t * 60f);
            }
            return d;
        }

        static float[] Whoosh(float len, float lpStart, float lpEnd)
        {
            var d = Buf(len);
            float lp = 0f, hp = 0f, prev = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float k = (float)i / d.Length;
                float n = White();
                lp += (n - lp) * Mathf.Lerp(lpStart, lpEnd, k);
                hp = 0.95f * (hp + lp - prev);
                prev = lp;
                d[i] = hp * Mathf.Sin(Mathf.PI * k);
            }
            return d;
        }

        static float[] Sweep(float len, float f0, float f1, float decay, float square)
        {
            var d = Buf(len);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float k = (float)i / d.Length;
                float f = Mathf.Lerp(f0, f1, k);
                phase += 2f * Mathf.PI * f / SR;
                float s = Mathf.Sin(phase);
                s = Mathf.Lerp(s, Mathf.Sign(s) * 0.7f, square);
                float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t * decay) * Mathf.Min(1f, (len - t) / 0.01f);
                d[i] = s * env;
            }
            return d;
        }

        static float[] Notes(float[] freqs, float each, float decay)
        {
            float len = freqs.Length * each + 0.35f;
            var d = Buf(len);
            for (int n = 0; n < freqs.Length; n++)
            {
                int start = Mathf.RoundToInt(n * each * SR);
                for (int i = start; i < d.Length; i++)
                {
                    float t = (float)(i - start) / SR;
                    float env = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t * decay);
                    float ph = 2f * Mathf.PI * freqs[n] * t;
                    d[i] += (Mathf.Sin(ph) + 0.3f * Mathf.Sin(ph * 2f) + 0.1f * Mathf.Sin(ph * 3f)) * env;
                }
            }
            return d;
        }

        static float[] Wail(float len, float f0, float f1, float vib, bool detune)
        {
            var d = Buf(len);
            float p1 = 0f, p2 = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float k = (float)i / d.Length;
                float f = Mathf.Lerp(f0, f1, k * k) * (1f + 0.04f * Mathf.Sin(t * vib * 2f * Mathf.PI));
                p1 += f / SR;
                p2 += f * (detune ? 1.03f : 1.5f) / SR;
                float saw = (p1 - Mathf.Floor(p1)) * 2f - 1f;
                float saw2 = (p2 - Mathf.Floor(p2)) * 2f - 1f;
                float s = saw + saw2 * 0.4f;
                lp += (s - lp) * 0.12f;
                float env = Mathf.Min(1f, t / 0.03f) * Mathf.Min(1f, (len - t) / 0.15f);
                d[i] = lp * env;
            }
            return d;
        }

        static float[] Shimmer(float len)
        {
            var d = Buf(len);
            float[] fs = { 1800f, 2400f, 3100f, 4200f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float s = 0f;
                for (int j = 0; j < fs.Length; j++)
                    s += Mathf.Sin(2f * Mathf.PI * fs[j] * t) * Mathf.Exp(-t * (5f + j * 3f));
                s += White() * 0.3f * Mathf.Exp(-t * 12f);
                d[i] = s;
            }
            return d;
        }

        static float[] Crackle(float len)
        {
            var d = Buf(len);
            float burst = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                if (rng.NextDouble() < 0.004) burst = 1f;
                burst *= 0.997f;
                d[i] = White() * (burst * 0.8f + 0.2f) * Mathf.Exp(-t * 6f) +
                       Mathf.Sin(2f * Mathf.PI * 2900f * t) * Mathf.Exp(-t * 14f) * 0.3f;
            }
            return d;
        }

        /// <summary>Filtro pasa-banda (biquad RBJ) para dar forma de vocal a la voz.</summary>
        class BandPass
        {
            float b0, b2, a1, a2;
            float x1, x2, y1, y2;

            public BandPass(float freq, float q) { Set(freq, q); }

            /// <summary>Cambia la frecuencia central sin perder el estado (formantes que se mueven).</summary>
            public void Set(float freq, float q)
            {
                float w0 = 2f * Mathf.PI * freq / SR;
                float alpha = Mathf.Sin(w0) / (2f * q);
                float a0 = 1f + alpha;
                b0 = alpha / a0;
                b2 = -alpha / a0;
                a1 = -2f * Mathf.Cos(w0) / a0;
                a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return y;
            }
        }

        /// <summary>Tipos de voz para los gritos de caida.</summary>
        public enum Voice { Neutral = 0, High = 1, Low = 2 }

        /// <summary>[voz][variante] gritos caricaturescos de caida.</summary>
        public static AudioClip[][] Screams;

        public static AudioClip GetScream(Voice v)
        {
            if (Screams == null) return null;
            var set = Screams[(int)v];
            return set[Random.Range(0, set.Length)];
        }

        static void GenerateScreams()
        {
            // (tono base, escala de formantes, velocidad del vibrato)
            var voices = new[]
            {
                (f0: 430f, fs: 1.0f, vib: 7.5f),   // neutra
                (f0: 720f, fs: 1.28f, vib: 9f),    // aguda (tipo dibujo animado chillon)
                (f0: 210f, fs: 0.8f, vib: 6f),     // grave
            };
            Screams = new AudioClip[voices.Length][];
            for (int v = 0; v < voices.Length; v++)
            {
                Screams[v] = new AudioClip[2];
                for (int k = 0; k < 2; k++)
                {
                    var p = voices[v];
                    Screams[v][k] = Clip("scream" + v + "_" + k, CartoonScream(2.3f, p.f0 * (k == 0 ? 1f : 1.07f), p.fs, p.vib, k), 0.8f);
                }
            }
            Scream = Screams[0][0];

            // "¡Oof!" al aterrizar tras un vuelo alto, con la misma voz.
            Oofs = new AudioClip[voices.Length];
            for (int v = 0; v < voices.Length; v++)
                Oofs[v] = Clip("oof" + v, Oof(0.38f, voices[v].f0 * 0.75f, voices[v].fs), 0.85f);
        }

        /// <summary>[voz] golpe de aterrizaje "oof".</summary>
        public static AudioClip[] Oofs;

        public static AudioClip GetOof(Voice v) => Oofs != null ? Oofs[(int)v] : null;

        /// <summary>
        /// "¡Oof!": golpe sordo + vocal U corta que cae de tono, como cuando a alguien
        /// le sacan el aire al caer de espaldas.
        /// </summary>
        static float[] Oof(float len, float baseF0, float formantScale)
        {
            var d = Buf(len);
            var f1 = new BandPass(380f * formantScale, 5f);
            var f2 = new BandPass(820f * formantScale, 7f);
            float ph = 0f, thumpPh = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float k = t / len;
                float f0 = baseF0 * Mathf.Lerp(1.15f, 0.7f, Mathf.Sqrt(k));
                ph += f0 / SR;
                float src = (ph - Mathf.Floor(ph)) * 2f - 1f + White() * 0.08f;
                float voice = (f1.Process(src) + f2.Process(src) * 0.5f) * Mathf.Min(1f, t / 0.012f) * Mathf.Exp(-t * 8f);

                // Golpe sordo del cuerpo contra el suelo.
                thumpPh += 2f * Mathf.PI * Mathf.Lerp(110f, 55f, Mathf.Min(1f, t / 0.12f)) / SR;
                lp += (White() - lp) * 0.2f;
                float thump = (Mathf.Sin(thumpPh) * 0.9f + lp * 0.6f) * Mathf.Exp(-t * 28f);
                d[i] = voice * 1.4f + thump;
            }
            return d;
        }

        /// <summary>
        /// Grito de caida caricaturesco: "¡wa-AAAAAaaaooo!". Arranca con un golpe de tono hacia arriba,
        /// luego cae en un glissando largo con vibrato cada vez mas exagerado, la vocal pasa de A a O
        /// y el volumen se desvanece como si se alejara.
        /// </summary>
        static float[] CartoonScream(float len, float baseF0, float formantScale, float vibRate, int variant)
        {
            var d = Buf(len);
            var f1 = new BandPass(850f * formantScale, 5f);
            var f2 = new BandPass(1300f * formantScale, 7f);
            var f3 = new BandPass(2700f * formantScale, 9f);
            float ph = 0f;
            float rise = variant == 0 ? 1.25f : 1.4f;  // cuanto sube al principio
            float drop = variant == 0 ? 0.45f : 0.38f; // donde termina el glissando
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float k = t / len;

                // Contorno: "wa" grave -> "AAAH" agudo (0.18 s) -> caida larga.
                float contour = t < 0.18f
                    ? Mathf.Lerp(0.8f, rise, Mathf.SmoothStep(0f, 1f, t / 0.18f))
                    : Mathf.Lerp(rise, drop, Mathf.Pow((t - 0.18f) / (len - 0.18f), 0.8f));
                float vibDepth = Mathf.Lerp(0.03f, 0.13f, k);
                float f0 = baseF0 * contour * (1f + vibDepth * Mathf.Sin(t * 2f * Mathf.PI * vibRate));
                ph += f0 / SR;
                float saw = (ph - Mathf.Floor(ph)) * 2f - 1f;
                float sq = Mathf.Sign(Mathf.Sin(ph * 2f * Mathf.PI)) * 0.35f; // un toque "nasal" de dibujo
                float src = saw + sq + White() * 0.05f;

                // Vocal A -> O a lo largo del grito.
                if ((i & 31) == 0)
                {
                    float vo = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.3f) / 1.2f));
                    f1.Set(Mathf.Lerp(850f, 560f, vo) * formantScale, 5f);
                    f2.Set(Mathf.Lerp(1300f, 900f, vo) * formantScale, 7f);
                }
                float y = f1.Process(src) + f2.Process(src) * 0.75f + f3.Process(src) * 0.25f;

                float env = Mathf.Min(1f, t / 0.03f) * (t < 0.45f ? 1f : Mathf.Exp(-(t - 0.45f) * 2.1f));
                d[i] = y * env;
            }
            return d;
        }

        static float[] Squelch(float len)
        {
            var d = Buf(len);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float k = (float)i / d.Length;
                lp += (White() - lp) * (0.05f + 0.1f * Mathf.Sin(k * Mathf.PI * 3f) * Mathf.Sin(k * Mathf.PI * 3f));
                d[i] = (lp * 3f + Mathf.Sin(2f * Mathf.PI * (200f - 120f * k) * t) * 0.6f) * Mathf.Sin(Mathf.PI * k);
            }
            return d;
        }
    }
}
