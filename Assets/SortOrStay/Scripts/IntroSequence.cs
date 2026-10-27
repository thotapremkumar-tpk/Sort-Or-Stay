using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 1 opening: fade in through the player's eyes while lying on the floor, stand up,
    /// then the camera glides back over the shoulder to reveal the avatar. Space skips it.
    public class IntroSequence : MonoBehaviour
    {
        public PlayerController player;
        public SortPuzzleBase puzzle;
        public bool playIntro = true;

        [TextArea] public string objective = DefaultObjective;

        public const string DefaultObjective =
            "Sort the 6 keys from <b>smallest to largest</b>, the bubble sort way.\n" +
            "Compare the <b>glowing pair</b>:  <b>swap</b> if the left key is bigger, otherwise they <b>stay</b>.\n" +
            "<size=20>Pick up a glowing key, then click its neighbour (swap) or the same key (stay)  ·  hook numbers 1-6  ·  H hint</size>";

        public string[] lines =
        {
            "...ugh. Where am I?",
            "One steel door. Six numbered keys on a rack.",
            "The terminal glows:  ORDER UNLOCKS."
        };

        bool skip;
        bool finished;

        /// Cut the intro short (same as pressing Space).
        public void Skip() { if (!finished) skip = true; }

        void Update()
        {
            var kb = Keyboard.current;
            if (!finished && kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) skip = true;
        }

        IEnumerator Start()
        {
            var ui = GameUI.Instance;
            if (!playIntro || player == null || player.cam == null)
            {
                Finish(ui);
                yield break;
            }

            player.canMove = false;
            player.canLook = false;
            player.cinematic = true;
            player.SetAvatarHidden(true);
            if (puzzle != null) puzzle.inputEnabled = false;

            Transform cam = player.cam.transform;
            Quaternion face = Quaternion.Euler(0f, player.Yaw, 0f);
            Vector3 basePos = player.transform.position;
            Vector3 lyingPos = basePos + face * new Vector3(0.18f, 0.26f, -0.15f);
            Quaternion lyingRot = face * Quaternion.Euler(-14f, 32f, 76f);
            Vector3 standPos = basePos + Vector3.up * player.EyeHeight;
            cam.position = lyingPos;
            cam.rotation = lyingRot;
            if (ui != null) { ui.SetFade(1f); ui.showHud = false; }

            yield return Wait(0.9f);
            if (ui != null) ui.Subtitle(lines[0], 3.4f);

            // eyes open: fade in with two slow blinks
            yield return FadeTo(ui, 0.35f, 1.1f);
            yield return FadeTo(ui, 0.85f, 0.35f);
            yield return FadeTo(ui, 0.0f, 1.4f);
            yield return Wait(0.5f);

            // stand up
            if (ui != null) ui.Subtitle(lines[1], 4.2f);
            float t = 0f, dur = 2.6f;
            while (t < dur && !skip)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                float wobble = Mathf.Sin(k * Mathf.PI) * 0.06f;
                cam.position = Vector3.Lerp(lyingPos, standPos, k) + new Vector3(0f, wobble, 0f);
                cam.rotation = Quaternion.Slerp(lyingRot, face, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (dur * 0.85f))));
                yield return null;
            }

            Finish(ui);
            if (!skip)
            {
                yield return new WaitForSeconds(1.6f);
                if (ui != null) ui.Subtitle(lines[2], 4f);
            }
        }

        void Finish(GameUI ui)
        {
            if (finished) return;
            finished = true;
            if (player != null)
            {
                player.SetAvatarHidden(false);
                player.SetLook(player.Yaw, 6f);
                player.cinematic = false;
                player.PullBackFromEye(skip ? 0.4f : 1.8f);
                player.canLook = true;
                player.canMove = true;
                if (!skip) player.Gesture(PlayerController.GestureLookAround);
            }
            if (puzzle != null) puzzle.inputEnabled = true;
            if (ui != null)
            {
                ui.SetFade(0f);
                ui.showHud = true;
                ui.SetObjective(objective);
                ui.ShowControls(14f);
            }
        }

        IEnumerator Wait(float seconds)
        {
            float e = 0f;
            while (e < seconds && !skip) { e += Time.deltaTime; yield return null; }
        }

        IEnumerator FadeTo(GameUI ui, float target, float seconds)
        {
            if (ui == null) yield break;
            float start = ui.Fade, e = 0f;
            while (e < seconds && !skip)
            {
                e += Time.deltaTime;
                ui.SetFade(Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, e / seconds)));
                yield return null;
            }
            if (!skip) ui.SetFade(target);
        }
    }
}
