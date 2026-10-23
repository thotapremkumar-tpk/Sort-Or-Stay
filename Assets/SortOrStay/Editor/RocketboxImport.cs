using System.IO;
using UnityEditor;
using UnityEngine;

namespace SortOrStayEditor
{
    /// Import settings for the Microsoft Rocketbox characters and animations.
    /// Only touches files under Assets/ThirdParty/Rocketbox, so every other model in the project imports as before.
    public class RocketboxImport : AssetPostprocessor
    {
        public const string ROOT = "Assets/ThirdParty/Rocketbox/";

        static bool Ours(string p) => p.Replace('\\', '/').StartsWith(ROOT);
        static bool IsAnim(string p) => p.Replace('\\', '/').Contains("/Animations/");

        void OnPreprocessTexture()
        {
            if (!Ours(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            string lower = assetPath.ToLowerInvariant();
            if (lower.Contains("_normal")) ti.textureType = TextureImporterType.NormalMap;
            if (lower.Contains("_opacity")) ti.alphaIsTransparency = true;
            ti.maxTextureSize = 2048;
        }

        void OnPreprocessModel()
        {
            if (!Ours(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.motionNodeName = "Bip01";
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            if (IsAnim(assetPath))
            {
                mi.importAnimation = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            }
            else
            {
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!Ours(assetPath) || !IsAnim(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            string stem = Path.GetFileNameWithoutExtension(assetPath).Replace(".max", "");
            bool locomotion = stem.Contains("_walk_") || stem.Contains("_run_");
            bool loop = locomotion || stem.Contains("idle_neutral");

            var c = clips[0];
            c.name = stem;
            c.loopTime = loop;
            c.loopPose = loop;
            c.lockRootRotation = true;          // bake rotation into the pose
            c.keepOriginalOrientation = true;
            c.lockRootHeightY = true;           // bake the up/down bob into the pose
            c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = !locomotion; // walk/run: forward travel becomes root motion, which we discard
            c.keepOriginalPositionXZ = true;
            mi.clipAnimations = new[] { c };
        }
    }
}
