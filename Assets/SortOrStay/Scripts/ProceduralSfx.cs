using System;
using System.Collections.Generic;
using UnityEngine;

namespace SortOrStay
{
    /// Every sound in the level is synthesised at runtime, so the project needs no audio files.
    public static class ProceduralSfx
    {
        const int SR = 44100;
        const float TAU = Mathf.PI * 2f;

        static readonly System.Random rng = new System.Random(20261013);
        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        static AudioClip select, swap, error, hint, unlock, rattle, slide, hum, thud, scan, page;
        static readonly Dictionary<int, AudioClip> blips = new Dictionary<int, AudioClip>();

        // ------------------------------------------------------------ clips

        public static AudioClip Select
        {
            get
            {
                if (select == null)
                    select = Make("sfx_select", 0.08f, t =>
                        0.45f * Mathf.Sin(TAU * 1250f * t) * Env(t, 0.004f, 0.025f));
                return select;
            }
        }

        public static AudioClip Swap
        {
            get
            {
                if (swap == null)
                    swap = Make("sfx_swap", 0.32f, t =>
                    {
                        float a = Clink(t, 2350f);
                        float b = t > 0.11f ? Clink(t - 0.11f, 2900f) : 0f;
                        return 0.35f * (a + b);
                    });
                return swap;
            }
        }

        public static AudioClip Error
        {
            get
            {
                if (error == null)
                    error = Make("sfx_error", 0.34f, t =>
                    {
                        bool on = t < 0.13f || (t > 0.18f && t < 0.31f);
                        float sq = Mathf.Sign(Mathf.Sin(TAU * 150f * t));
                        return on ? 0.16f * sq : 0f;
                    });
                return error;
            }
        }

        public static AudioClip Hint
        {
            get
            {
                if (hint == null)
                    hint = Make("sfx_hint", 0.42f, t =>
                    {
                        float f = t < 0.16f ? 740f : 988f;
                        float local = t < 0.16f ? t : t - 0.16f;
                        return 0.32f * Mathf.Sin(TAU * f * t) * Env(local, 0.005f, 0.12f);
                    });
                return hint;
            }
        }

        public static AudioClip Unlock
        {
            get
            {
                if (unlock == null)
                    unlock = Make("sfx_unlock", 1.25f, t =>
                    {
                        float clunk = (0.7f * Mathf.Sin(TAU * 70f * t) + 0.4f * Noise()) * Env(t, 0.002f, 0.07f);
                        float chime = 0f;
                        float[] notes = { 659.25f, 783.99f, 1046.5f };
                        for (int i = 0; i < notes.Length; i++)
                        {
                            float st = 0.28f + i * 0.12f;
                            if (t > st) chime += Mathf.Sin(TAU * notes[i] * t) * Env(t - st, 0.004f, 0.35f);
                        }
                        return 0.5f * clunk + 0.22f * chime;
                    });
                return unlock;
            }
        }

        public static AudioClip Rattle
        {
            get
            {
                if (rattle == null)
                {
                    float[] hits = { 0.00f, 0.07f, 0.12f, 0.20f, 0.26f, 0.35f, 0.41f };
                    rattle = Make("sfx_rattle", 0.62f, t =>
                    {
                        float s = 0f;
                        foreach (float h in hits)
                            if (t >= h) s += (0.6f * Noise() + 0.5f * Mathf.Sin(TAU * 1900f * t)) * Env(t - h, 0.001f, 0.03f);
                        s += 0.5f * Mathf.Sin(TAU * 85f * t) * Env(t, 0.003f, 0.12f);
                        return 0.35f * s;
                    });
                }
                return rattle;
            }
        }

        public static AudioClip Slide
        {
            get
            {
                if (slide == null)
                {
                    float lp = 0f;
                    slide = Make("sfx_slide", 1.6f, t =>
                    {
                        lp += (Noise() - lp) * 0.035f;
                        float env = Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((1.6f - t) / 0.35f);
                        return env * (1.8f * lp + 0.12f * Mathf.Sin(TAU * 48f * t));
                    });
                }
                return slide;
            }
        }

        /// Seamless 1 s loop: electrical hum of the ceiling light.
        public static AudioClip Hum
        {
            get
            {
                if (hum == null)
                    hum = Make("sfx_hum", 1f, t =>
                        0.20f * Mathf.Sin(TAU * 60f * t) + 0.10f * Mathf.Sin(TAU * 120f * t) + 0.04f * Mathf.Sin(TAU * 180f * t));
                return hum;
            }
        }

        /// Low wooden thump: a crate set down on the floor.
        public static AudioClip Thud
        {
            get
            {
                if (thud == null)
                    thud = Make("sfx_thud", 0.45f, t =>
                        (0.8f * Mathf.Sin(TAU * 62f * t) + 0.35f * Mathf.Sin(TAU * 131f * t) + 0.25f * Noise()) * Env(t, 0.002f, 0.07f) * 0.6f);
                return thud;
            }
        }

        /// Two quick scale beeps: a crate has been weighed.
        public static AudioClip Scan
        {
            get
            {
                if (scan == null)
                    scan = Make("sfx_scan", 0.26f, t =>
                    {
                        float a = Mathf.Sin(TAU * 1760f * t) * Env(t, 0.003f, 0.03f);
                        float b = t > 0.11f ? Mathf.Sin(TAU * 2349f * t) * Env(t - 0.11f, 0.003f, 0.04f) : 0f;
                        return 0.28f * (a + b);
                    });
                return scan;
            }
        }

        /// Soft papery whoosh: a book sliding onto the shelf.
        public static AudioClip Page
        {
            get
            {
                if (page == null)
                {
                    float lp = 0f;
                    page = Make("sfx_page", 0.42f, t =>
                    {
                        lp += (Noise() - lp) * 0.25f;
                        float env = Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-t / 0.12f);
                        return 0.9f * lp * env + 0.3f * Mathf.Sin(TAU * 180f * t) * Env(t - 0.3f, 0.002f, 0.04f);
                    });
                }
                return page;
            }
        }

        /// Rising pentatonic notes for the "keys light up left to right" moment.
        public static AudioClip Blip(int step)
        {
            if (blips.TryGetValue(step, out var c) && c != null) return c;
            int[] semis = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21 };
            float f = 523.25f * Mathf.Pow(2f, semis[Mathf.Clamp(step, 0, semis.Length - 1)] / 12f);
            c = Make("sfx_blip_" + step, 0.35f, t => 0.3f * Mathf.Sin(TAU * f * t) * Env(t, 0.003f, 0.12f));
            blips[step] = c;
            return c;
        }

        // ------------------------------------------------------------ playback

        public static void Play(AudioClip clip, Vector3 pos, float volume = 0.8f)
        {
            if (clip != null) AudioSource.PlayClipAtPoint(clip, pos, volume);
        }

        /// Plays right at the listener (UI feedback).
        public static void PlayUI(AudioClip clip, float volume = 0.6f)
        {
            var cam = Camera.main;
            Play(clip, cam != null ? cam.transform.position : Vector3.zero, volume);
        }

        // ------------------------------------------------------------ synthesis helpers

        static float Env(float t, float attack, float decay)
        {
            if (t < 0f) return 0f;
            return Mathf.Clamp01(t / attack) * Mathf.Exp(-t / decay);
        }

        static float Clink(float t, float f)
        {
            return (Mathf.Sin(TAU * f * t) + 0.55f * Mathf.Sin(TAU * f * 1.51f * t) + 0.3f * Mathf.Sin(TAU * f * 2.37f * t))
                   * Env(t, 0.001f, 0.045f);
        }

        static AudioClip Make(string name, float seconds, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * SR);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)SR), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, SR, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
