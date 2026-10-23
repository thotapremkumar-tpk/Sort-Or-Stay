#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SortOrStay
{
    /// Editor-only helper for presentation screenshots.
    /// In Play mode press K (or use the menu Sort or Stay > Capture Game Screenshot) and the Game view is saved,
    /// HUD included, to  <project>/Screenshots/<scene>_<time>.png  at twice the Game view size.
    public class DevScreenshot : MonoBehaviour
    {
        public const string Folder = "Screenshots";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<DevScreenshot>() != null) return;
            var go = new GameObject("DevScreenshot (editor only)");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            go.AddComponent<DevScreenshot>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.kKey.wasPressedThisFrame) Capture();
        }

        public static string Capture()
        {
            Directory.CreateDirectory(Folder);
            string scene = SceneManager.GetActiveScene().name;
            string file = Path.Combine(Folder, $"{scene}_{DateTime.Now:HHmmss_fff}.png");
            ScreenCapture.CaptureScreenshot(file, 2);
            Debug.Log("[DevScreenshot] saved " + Path.GetFullPath(file));
            return file;
        }
    }
}
#endif
