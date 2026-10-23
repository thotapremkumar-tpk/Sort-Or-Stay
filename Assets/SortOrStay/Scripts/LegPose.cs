using UnityEngine;

namespace SortOrStay
{
    /// Jump poses for the avatars (there is no jump clip): the hips sink while the feet stay planted for the
    /// crouch before take-off and the landing, and the feet tuck up toward the hips in the air.
    [DefaultExecutionOrder(90)]           // after PlayerController.LateUpdate, before HandReach
    public class LegPose : MonoBehaviour
    {
        public float crouchDepth = 0.13f; // metres the hips drop at a full crouch
        public float tuckLift = 0.2f;     // metres the feet rise at a full tuck

        static readonly string[] Prefix = { "Bip01 R ", "Bip01 L " };

        PlayerController pc;
        Transform[,] thigh, calf, foot;   // [avatar, side]

        void Awake()
        {
            pc = GetComponent<PlayerController>();
            int n = pc != null && pc.avatars != null ? pc.avatars.Length : 0;
            thigh = new Transform[n, 2];
            calf = new Transform[n, 2];
            foot = new Transform[n, 2];
            for (int i = 0; i < n; i++)
            {
                if (pc.avatars[i] == null) continue;
                for (int s = 0; s < 2; s++)
                {
                    thigh[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Thigh");
                    calf[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Calf");
                    foot[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Foot");
                }
            }
        }

        void LateUpdate()
        {
            if (pc == null) return;
            float crouch = pc.JumpCrouch, tuck = pc.JumpTuck;
            if (crouch <= 0.001f && tuck <= 0.001f) return;

            int a = pc.AvatarIndex;
            if (a >= thigh.GetLength(0) || pc.rootBones == null || a >= pc.rootBones.Length || pc.rootBones[a] == null) return;

            // where the animation put the feet, before the hips move
            var planted = new Vector3[2];
            for (int s = 0; s < 2; s++) if (foot[a, s] != null) planted[s] = foot[a, s].position;

            pc.rootBones[a].position += Vector3.down * (crouchDepth * crouch);

            Transform body = pc.Body;
            for (int s = 0; s < 2; s++)
            {
                if (thigh[a, s] == null || calf[a, s] == null || foot[a, s] == null) continue;
                Vector3 goal = planted[s] + Vector3.up * (tuckLift * tuck);
                TwoBoneIK.Solve(thigh[a, s], calf[a, s], foot[a, s], goal, body.forward, 1f);   // knees bend forward
            }
        }
    }
}
