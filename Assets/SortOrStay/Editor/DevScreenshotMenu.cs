using UnityEditor;
using UnityEngine;

namespace SortOrStay.EditorTools
{
    /// Menu twin of the K key: saves the Game view (Play mode only) into <project>/Screenshots.
    public static class DevScreenshotMenu
    {
        [MenuItem("Sort or Stay/Capture Game Screenshot")]
        static void Capture() => SortOrStay.DevScreenshot.Capture();

        [MenuItem("Sort or Stay/Capture Game Screenshot", true)]
        static bool CanCapture() => Application.isPlaying;
    }
}
