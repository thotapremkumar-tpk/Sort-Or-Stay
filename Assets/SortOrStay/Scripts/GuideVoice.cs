using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SortOrStay
{
    /// Professor Ada's voice: each line she says is read aloud by the computer's own text-to-speech
    /// (macOS: the built-in `say` voices; Windows: System.Speech through PowerShell). Text-to-speech instead of
    /// recorded clips, because many of her lines are made on the spot (hints with the room's real numbers).
    /// A new line cuts the previous one off. On / off is remembered (pause menu, or the dialog box).
    ///
    /// The text goes to the speech engine through its standard input (no temp files, no quoting problems).
    /// The first lines of each play session are logged with how long they took, so the Console shows whether
    /// she was really heard; a muted Mac is detected and shown in her dialog box.
    public static class GuideVoice
    {
        const string Key = "SortOrStay.GuideVoice";
        const int Rate = 180;                          // words per minute (macOS)

        /// Preferred voices, in order (the first one installed is used; none: the system voice).
        static readonly string[] Preferred =
        {
            "Ava (Premium)", "Ava (Enhanced)", "Zoe (Premium)", "Zoe (Enhanced)", "Samantha (Enhanced)", "Allison (Enhanced)",
            "Susan (Enhanced)", "Isha (Enhanced)", "Samantha", "Ava", "Zoe", "Isha", "Veena", "Karen", "Moira", "Tessa",
            "Serena", "Fiona", "Victoria", "Allison"
        };

        public static bool On
        {
            get => PlayerPrefs.GetInt(Key, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
                if (!value) Stop();
            }
        }

        static bool IsMac => Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer;
        static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer;
        public static bool Supported => (IsMac || IsWindows) && !failed;

        /// The name of the macOS voice in use (null = the system default).
        public static string VoiceName => voice;

        /// Something on this computer keeps her from being heard (e.g. the Mac's sound is muted), or null.
        public static string Problem => problem;

        class Line
        {
            public Process p;
            public Stopwatch clock;
            public int chars;
            public string voice;
            public volatile bool cut;
            public readonly StringBuilder err = new StringBuilder();
        }

        static Line current;
        static volatile string voice = "Samantha";
        static volatile bool failed;
        static volatile string problem;
        static bool problemShown;
        static int logged, failures;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            current = null;
            logged = 0;
            failures = 0;
            failed = false;
            problemShown = false;
            Application.quitting -= Stop;
            Application.quitting += Stop;
        }

        /// At the start of a game: which voice to use, and is the Mac's sound on? (off the main thread)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!IsMac && !IsWindows)
            {
                Debug.Log("Sort or Stay: no speech engine on this platform, Professor Ada's lines stay on screen only.");
                return;
            }
            Debug.Log("Sort or Stay: Professor Ada's voice is " + (On ? "on" : "OFF (turn it on in the pause menu: Esc > Ada's voice)") + ".");
            if (IsMac) ThreadPool.QueueUserWorkItem(_ => { Probe(); CheckMacSound(); });
        }

        /// Is she still speaking the last line?
        public static bool Speaking
        {
            get
            {
                var l = current;
                if (l == null) return false;
                // a line never "speaks" for longer than it possibly could (keeps the lesson from waiting forever)
                if (l.clock.Elapsed.TotalSeconds > 6.0 + l.chars / 8.0) return false;
                try { return !l.p.HasExited; }
                catch (Exception) { return false; }
            }
        }

        /// Say this line aloud (rich text is cleaned up first). Cuts off whatever she was saying.
        public static void Speak(string rich)
        {
            Stop();
            if (!On || !Supported) return;
#if UNITY_EDITOR
            if (DevAutoPlay.On) return;                 // the play-test tool runs silent
#endif
            string text = ForSpeech(rich);
            if (string.IsNullOrWhiteSpace(text)) return;

            if (problem != null && !problemShown && GameUI.Instance != null)
            {
                problemShown = true;
                GameUI.Instance.Flash(problem, new Color(1f, 0.82f, 0.35f), 7f);
            }

            var line = new Line { chars = text.Length, voice = IsMac ? voice : null };
            try
            {
                var psi = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                };
                if (IsMac)
                {
                    psi.FileName = "/usr/bin/say";
                    psi.Arguments = (line.voice != null ? "-v \"" + line.voice + "\" " : "") + "-r " + Rate;   // no text argument: reads stdin
                }
                else
                {
                    psi.FileName = "powershell";
                    psi.Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Add-Type -AssemblyName System.Speech; " +
                                    "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                                    "try { $s.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::Female) } catch {}; " +
                                    "$s.Speak([Console]::In.ReadToEnd())\"";
                }
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (line.err) line.err.AppendLine(e.Data); };
                p.Exited += (s, e) => Finished(line);
                line.p = p;
                line.clock = Stopwatch.StartNew();
                p.Start();
                current = line;
                p.BeginErrorReadLine();
                byte[] bytes = new UTF8Encoding(false).GetBytes(text + "\n");
                var stdin = p.StandardInput.BaseStream;
                stdin.Write(bytes, 0, bytes.Length);
                stdin.Flush();
                p.StandardInput.Close();
            }
            catch (Exception e)
            {
                current = null;
                failed = true;
                Debug.LogWarning("Sort or Stay: Professor Ada's voice is not available on this computer (" + e.Message + "). Her lines stay on screen.");
            }
        }

        /// A line has ended by itself (not cut off): log the first few, and react to errors. Runs off the main thread.
        static void Finished(Line line)
        {
            if (line.cut) return;
            int code;
            double secs;
            string err;
            try
            {
                line.p.WaitForExit();                    // lets the error stream finish too
                code = line.p.ExitCode;
                secs = line.clock.Elapsed.TotalSeconds;
                lock (line.err) err = line.err.ToString().Trim();
            }
            catch (Exception) { return; }
            if (line.cut) return;

            if (code != 0)
            {
                int n = Interlocked.Increment(ref failures);
                Debug.LogWarning($"Sort or Stay: Professor Ada's voice: the speech engine stopped with code {code} after {secs:0.0} s" +
                                 (err.Length > 0 ? ": " + err.TrimEnd('.') : "") + ".");
                if (line.voice != null && line.voice == voice)
                {
                    voice = null;                        // that voice is the trouble: use the system voice from now on
                    Debug.LogWarning("Sort or Stay: switching Professor Ada to the system's default voice.");
                }
                else if (n >= 3) failed = true;
                return;
            }
            if (line.chars > 30 && secs < 0.3)
                Debug.LogWarning($"Sort or Stay: Professor Ada's line ({line.chars} characters) finished in {secs:0.00} s, too fast to have been heard. " +
                                 "Check the computer's sound output.");
            else if (Interlocked.Increment(ref logged) <= 3)
                Debug.Log($"Sort or Stay: Professor Ada said a line aloud ({line.chars} characters in {secs:0.0} s, voice {line.voice ?? "system default"}).");
        }

        /// Stop talking now.
        public static void Stop()
        {
            var l = current;
            current = null;
            if (l == null) return;
            l.cut = true;
            try { if (!l.p.HasExited) l.p.Kill(); }
            catch (Exception) { }
        }

        /// macOS: pick an installed English voice from the preferred list (asks `say -v ?` once per game).
        static void Probe()
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/say", "-v ?") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    string list = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(4000);
                    foreach (var name in Preferred)
                        if (Regex.IsMatch(list, "^" + Regex.Escape(name) + @"\s+en[_-]", RegexOptions.Multiline))
                        {
                            voice = name;
                            Debug.Log("Sort or Stay: Professor Ada speaks with the macOS voice \"" + name + "\".");
                            return;
                        }
                    voice = null;
                    Debug.Log("Sort or Stay: Professor Ada speaks with the Mac's default voice.");
                }
            }
            catch (Exception e)
            {
                voice = null;
                Debug.LogWarning("Sort or Stay: could not list the Mac's voices (" + e.Message + "); using the default voice.");
            }
        }

        /// macOS: is the sound output muted, or the volume at zero? Then she can't be heard: say so.
        static void CheckMacSound()
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/osascript", "-e \"get volume settings\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(4000);
                    Debug.Log("Sort or Stay: Mac sound settings: " + s);
                    var vol = Regex.Match(s, @"output volume:(\d+)");
                    bool muted = s.Contains("output muted:true");
                    if (muted) problem = "Your Mac's sound is muted.  Unmute it to hear Professor Ada.";
                    else if (vol.Success && vol.Groups[1].Value == "0") problem = "Your Mac's volume is at zero.  Turn it up to hear Professor Ada.";
                }
            }
            catch (Exception) { }
        }

        /// Rich text to something a speech engine reads well: no tags, maths read out in words, plain ASCII.
        public static string ForSpeech(string rich)
        {
            if (string.IsNullOrEmpty(rich)) return "";
            string s = Regex.Replace(rich, "<[^>]+>", "");
            s = s.Replace("\n", ". ");
            s = s.Replace("n·log₂n − n + 1", "n log base 2 of n, minus n, plus 1");
            s = s.Replace("O(n²)", "O of n squared").Replace("O(n log n)", "O of n log n").Replace("O(log n)", "O of log n")
                 .Replace("O(n)", "O of n").Replace("O(1)", "O of 1");
            s = s.Replace("n(n−1)/2", "n times n minus 1, over 2").Replace("n(n-1)/2", "n times n minus 1, over 2")
                 .Replace("n(n−1)/4", "n times n minus 1, over 4").Replace("n(n-1)/4", "n times n minus 1, over 4")
                 .Replace("n²/4", "n squared over 4").Replace("n²/2", "n squared over 2").Replace("n²", "n squared");
            s = Regex.Replace(s, @"[ \t]{2,}·[ \t]{2,}", ",  ");         // "H hint  ·  T lesson": a separator
            s = s.Replace("·", " times ");                                // "n · log₂n", "8 · 7 / 2": multiplication
            s = Regex.Replace(s, @"log₂\s*(\w+)", "log base 2 of $1");
            s = s.Replace("log₂", "log base 2");
            s = Regex.Replace(s, @"\b([a-z]|\d+)\s*/\s*(\d+|[a-z])\b", "$1 over $2");
            s = s.Replace("−", " minus ").Replace("→", " to ").Replace("←", " ").Replace("≈", " about ").Replace("×", " times ")
                 .Replace("…", ", ").Replace("—", ", ").Replace("–", ", ").Replace("‘", "'").Replace("’", "'")
                 .Replace("“", "\"").Replace("”", "\"").Replace("↓", "").Replace("▶", "").Replace("◀", "")
                 .Replace("✓", "").Replace("✗", "");
            s = Regex.Replace(s, @"\s=\s", " equals ");
            s = Regex.Replace(s, @"[^\u0000-\u007F]", " ");
            s = Regex.Replace(s, @"(\.\s*){2,}", ". ");
            s = Regex.Replace(s, @"\s+([,.:;!?])", "$1");
            s = Regex.Replace(s, @"([,.:;])(?:\s*[,.:;])+", "$1");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            return s;
        }
    }
}
