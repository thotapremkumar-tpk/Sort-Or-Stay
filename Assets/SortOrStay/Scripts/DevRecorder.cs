#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor.Media;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SortOrStay
{
    /// Editor-only gameplay recorder (menu Sort or Stay > Dev: Record the gameplay video). Runs together with the
    /// auto-play: every frame of the Game view goes into an H.264 MP4 at a fixed 30 fps (the game clock steps
    /// 1/30 s per frame however slow the recording is, so the video is smooth), with a sound track mixed here:
    /// Professor Ada's lines (the macOS voice rendered to a file, not out loud) and the game's synthesised sounds.
    /// The file goes to Recordings/ in the project folder (git-ignored). Never part of a build.
    public class DevRecorder : MonoBehaviour
    {
        public const string Key = "SortOrStay.DevRecord";
        public static bool On => UnityEditor.SessionState.GetBool(Key, false);
        public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../Recordings"));
        public static string LastFile { get; private set; }

        public const int FPS = 24;
        public const int SR = 44100;
        const int MaxSamplesPerFrame = SR / FPS + 1;    // 1838 (44100 / 24 = 1837.5: alternate frames get one more)

        static DevRecorder instance;
        MediaEncoder encoder;
        Texture2D frame;
        int width, height;
        long frames;
        bool finished;
        float[] mix = new float[MaxSamplesPerFrame];
        int frameSamples;

        class Sound { public float[] data; public int pos; public float gain; }
        Sound voice;
        readonly List<Sound> sfx = new List<Sound>();
        readonly Dictionary<AudioClip, float[]> clipData = new Dictionary<AudioClip, float[]>();
        int voiceIndex;

        /// Seconds of video recorded so far (the clock the auto-play paces itself by while recording).
        public static float Clock => instance != null ? instance.frames / (float)FPS : Time.unscaledTime;

        /// Is Ada's current line still playing in the recording?
        public static bool VoiceBusy => instance != null && instance.voice != null && instance.voice.pos < instance.voice.data.Length;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!On || instance != null) return;
            var go = new GameObject("DevRecorder");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DevRecorder>();
        }

        void Start()
        {
            Application.runInBackground = true;
            Time.captureFramerate = FPS;
            width = Screen.width & ~1;
            height = Screen.height & ~1;
            frame = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Directory.CreateDirectory(Folder);
            LastFile = Path.Combine(Folder, "SortOrStay_raw.mp4");     // one file, replaced by each new recording
            var v = new VideoTrackAttributes
            {
                frameRate = new MediaRational(FPS),
                width = (uint)width,
                height = (uint)height,
                includeAlpha = false,
                bitRateMode = UnityEditor.VideoBitrateMode.Medium
            };
            var a = new AudioTrackAttributes { sampleRate = new MediaRational(SR), channelCount = 1, language = "en" };
            try { encoder = new MediaEncoder(LastFile, v, a); }
            catch (Exception e)
            {
                Debug.LogError("Sort or Stay: could not start the recording (" + e.Message + ")");
                enabled = false;
                return;
            }
            ProceduralSfx.Played += OnSfx;
            DevAutoPlay.Log($"=== recording {width}x{height} at {FPS} fps to {LastFile}");
            Debug.Log($"Sort or Stay: recording the gameplay video ({width}x{height}, {FPS} fps) to {LastFile}");
            StartCoroutine(Capture());
        }

        IEnumerator Capture()
        {
            var eof = new WaitForEndOfFrame();
            float realStart = Time.realtimeSinceStartup;
            while (!finished)
            {
                yield return eof;
                if (finished || encoder == null) break;
                frame.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                frame.Apply(false);
                encoder.AddFrame(frame);
                MixAudio();
                frames++;
                if (frames % (FPS * 60) == 0)
                    DevAutoPlay.Log($"=== recorded {frames / FPS / 60} min of video  (real time {(Time.realtimeSinceStartup - realStart) / 60f:0.0} min, unscaledTime step {Time.unscaledDeltaTime:0.0000})");
            }
        }

        void MixAudio()
        {
            frameSamples = (int)((frames + 1) * SR / FPS - frames * SR / FPS);
            Array.Clear(mix, 0, mix.Length);
            if (voice != null) Add(voice, true);
            for (int i = sfx.Count - 1; i >= 0; i--)
                if (!Add(sfx[i], false)) sfx.RemoveAt(i);
            var na = new NativeArray<float>(frameSamples, Allocator.Temp);
            for (int i = 0; i < frameSamples; i++) na[i] = Mathf.Clamp(mix[i], -1f, 1f);
            encoder.AddSamples(na);
            na.Dispose();
        }

        bool Add(Sound s, bool isVoice)
        {
            int n = Mathf.Min(frameSamples, s.data.Length - s.pos);
            for (int i = 0; i < n; i++) mix[i] += s.data[s.pos + i] * s.gain;
            s.pos += Mathf.Max(0, n);
            return s.pos < s.data.Length;
        }

        void OnSfx(AudioClip clip, Vector3 pos, float volume)
        {
            if (clip == null || finished) return;
            if (!clipData.TryGetValue(clip, out var d))
            {
                d = new float[clip.samples * clip.channels];
                try { clip.GetData(d, 0); } catch (Exception) { return; }
                if (clip.channels > 1)
                {
                    var mono = new float[clip.samples];
                    for (int i = 0; i < clip.samples; i++) mono[i] = d[i * clip.channels];
                    d = mono;
                }
                clipData[clip] = d;
            }
            var cam = Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, pos) : 1f;
            float gain = volume * 0.55f * Mathf.Clamp01(1.6f / Mathf.Max(1f, dist));
            if (gain < 0.01f) return;
            sfx.Add(new Sound { data = d, pos = 0, gain = gain });
        }

        // ------------------------------------------------------------ Ada's voice (rendered, not spoken)

        /// Render this line with the macOS voice and start it in the recording (cuts off the previous line).
        public static void Say(string text, string voiceName)
        {
            if (instance == null || instance.finished) return;
            instance.voice = null;
            var data = Render(text, voiceName, ++instance.voiceIndex);
            if (data != null) instance.voice = new Sound { data = data, pos = 0, gain = 1f };
        }

        public static void StopVoice()
        {
            if (instance != null) instance.voice = null;
        }

        static float[] Render(string text, string voiceName, int n)
        {
            if (Application.platform != RuntimePlatform.OSXEditor) return null;
            string wav = Path.Combine(Application.temporaryCachePath, $"ada_rec_{n % 3}.wav");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/say",
                    Arguments = (voiceName != null ? "-v \"" + voiceName + "\" " : "") + "-r " + GuideVoice.MacRate + " --file-format=WAVE --data-format=LEI16@" + SR + " -o \"" + wav + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                };
                using (var p = Process.Start(psi))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text + "\n");
                    p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    p.StandardInput.Close();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch (Exception) { } return null; }
                }
                return ReadWav(File.ReadAllBytes(wav));
            }
            catch (Exception e)
            {
                DevAutoPlay.Log("  voice render failed: " + e.Message);
                return null;
            }
        }

        /// 16-bit PCM WAV to floats (first channel), with a little level so she sits above the sound effects.
        static float[] ReadWav(byte[] b)
        {
            int pos = 12, channels = 1, bits = 16;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(b, pos + 10);
                    bits = BitConverter.ToInt16(b, pos + 22);
                }
                else if (id == "data")
                {
                    if (bits != 16) return null;
                    size = Mathf.Min(size, b.Length - pos - 8);
                    int count = size / 2 / channels;
                    var f = new float[count];
                    float peak = 0.001f;
                    for (int i = 0; i < count; i++)
                    {
                        f[i] = BitConverter.ToInt16(b, pos + 8 + i * 2 * channels) / 32768f;
                        peak = Mathf.Max(peak, Mathf.Abs(f[i]));
                    }
                    float g = Mathf.Min(4f, 0.85f / peak);
                    for (int i = 0; i < count; i++) f[i] *= g;
                    return f;
                }
                pos += 8 + size + (size & 1);
            }
            return null;
        }

        // ------------------------------------------------------------ the end

        /// Close the video file and leave Play mode.
        public static void Finish()
        {
            if (instance == null || instance.finished) return;
            instance.Close();
            UnityEditor.EditorApplication.isPlaying = false;
        }

        void Close()
        {
            if (finished) return;
            finished = true;
            ProceduralSfx.Played -= OnSfx;
            Time.captureFramerate = 0;
            if (encoder != null)
            {
                try { encoder.Dispose(); } catch (Exception e) { Debug.LogWarning("Sort or Stay: closing the video: " + e.Message); }
                encoder = null;
                DevAutoPlay.Log($"=== recording saved: {LastFile}  ({frames} frames, {frames / (float)FPS / 60f:0.0} min)");
                Debug.Log($"Sort or Stay: gameplay video saved to {LastFile} ({frames / (float)FPS / 60f:0.0} min).");
            }
        }

        void OnDisable() { Close(); }
        void OnApplicationQuit() { Close(); }
        void OnDestroy()
        {
            Close();
            if (instance == this) instance = null;
            if (frame != null) Destroy(frame);
        }
    }
}
#endif
