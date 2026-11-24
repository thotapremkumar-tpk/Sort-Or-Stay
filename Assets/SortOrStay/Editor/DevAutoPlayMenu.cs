using System.IO;
using SortOrStay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SortOrStayEditor
{
    /// Editor-only play-testing. Auto-play: the floor plays itself (next correct step every half second).
    /// Hands-on test: every step goes through the real click / number-key path (the avatar walks over and uses
    /// the thing), with deliberate wrong moves and hints. Both close the debrief, walk out and carry on until
    /// the end card. The log goes to Logs/SortOrStay_autoplay.txt, screenshots to Screenshots/.
    /// Switches itself off when Play mode ends.
    [InitializeOnLoad]
    public static class DevAutoPlayMenu
    {
        static DevAutoPlayMenu()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(DevAutoPlay.PrefKey, false))
            {
                SessionState.SetBool(DevAutoPlay.PrefKey, false);
                SessionState.SetBool(DevAutoPlay.HandsKey, false);
                Debug.Log("Sort or Stay: auto-play switched off.  Log: " + DevAutoPlay.LogPath);
            }
            if (s == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(DevRecorder.Key, false))
            {
                SessionState.SetBool(DevRecorder.Key, false);
                Debug.Log("Sort or Stay: recording switched off.  Video: " + DevRecorder.LastFile);
            }
        }

        const string Building = Kit.ROOT + "Scenes/Building.unity";

        /// The whole building played hands-on (the avatar walks to every key, crate, book, block and bar and uses it;
        /// the camera follows the sorting, first person close up), recorded to Recordings/ as an MP4 with Ada's voice
        /// and the game sounds. Keep the Game view at 1920x1080 while it records.
        [MenuItem("Sort or Stay/Dev: Record the gameplay video (whole building)", false, 180)]
        static void RecordBuilding() { Start(Building, true, 1, true); }

        [MenuItem("Sort or Stay/Dev: Auto-play the building (top to street)", false, 190)]
        static void BuildingAuto() { Start(Building, false); }

        [MenuItem("Sort or Stay/Dev: Auto-play the building from Floor 4", false, 191)]
        static void BuildingAutoFrom4() { Start(Building, false, 4); }

        [MenuItem("Sort or Stay/Dev: Hands-on test the building", false, 192)]
        static void BuildingHands() { Start(Building, true); }

        [MenuItem("Sort or Stay/Dev: Auto-play Floors 4 and 5", false, 200)]
        static void FromFloor4() { Start(Kit.ROOT + "Scenes/Level4_Merge.unity", false); }

        [MenuItem("Sort or Stay/Dev: Auto-play Floor 5", false, 201)]
        static void FromFloor5() { Start(Kit.ROOT + "Scenes/Level5_Quick.unity", false); }

        [MenuItem("Sort or Stay/Dev: Hands-on test Floors 4 and 5", false, 202)]
        static void HandsFromFloor4() { Start(Kit.ROOT + "Scenes/Level4_Merge.unity", true); }

        static void Start(string scenePath, bool hands, int floor = 1, bool record = false)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("Sort or Stay: stop Play mode first."); return; }
            if (!File.Exists(scenePath)) { Debug.LogError("Sort or Stay: build the floor first, " + scenePath + " does not exist."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DevAutoPlay.LogPath));
                File.WriteAllText(DevAutoPlay.LogPath, $"Sort or Stay {(record ? "recording" : hands ? "hands-on test" : "auto-play")}  {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  from {scenePath}\n");
            }
            catch (System.Exception e) { Debug.LogWarning("Sort or Stay: could not reset the auto-play log. " + e.Message); }
            SessionState.SetBool(DevAutoPlay.PrefKey, true);
            SessionState.SetBool(DevAutoPlay.HandsKey, hands);
            SessionState.SetInt(DevAutoPlay.FloorKey, floor);
            SessionState.SetBool(DevRecorder.Key, record);
            EditorSceneManager.OpenScene(scenePath);
            EditorApplication.EnterPlaymode();
        }
    }
}
