using System.Collections;
using UnityEngine;

namespace SortOrStay
{
    /// Ceiling light that mostly holds steady, then stutters every few seconds, with an electrical hum.
    public class FlickerLight : MonoBehaviour
    {
        public Light lamp;
        public Renderer bulb;
        public Color emission = new Color(1f, 0.86f, 0.62f);
        public float baseIntensity = 2.5f;
        public float emissionStrength = 2.2f;
        public bool hum = true;
        public Vector2 calmSeconds = new Vector2(3f, 8f);

        Material mat;
        AudioSource buzz;
        float nextBurst;

        void Start()
        {
            if (bulb != null)
            {
                mat = bulb.material;
                mat.EnableKeyword("_EMISSION");
            }
            if (hum)
            {
                buzz = gameObject.AddComponent<AudioSource>();
                buzz.clip = ProceduralSfx.Hum;
                buzz.loop = true;
                buzz.spatialBlend = 1f;
                buzz.volume = 0.14f;
                buzz.minDistance = 1.2f;
                buzz.maxDistance = 12f;
                buzz.rolloffMode = AudioRolloffMode.Linear;
                buzz.Play();
            }
            nextBurst = Time.time + Random.Range(2.5f, 4.5f);
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            while (true)
            {
                Set(1f + (Mathf.PerlinNoise(Time.time * 2.7f, 0.3f) - 0.5f) * 0.08f);
                if (Time.time >= nextBurst)
                {
                    int flicks = Random.Range(2, 6);
                    for (int i = 0; i < flicks; i++)
                    {
                        Set(Random.Range(0.04f, 0.3f));
                        yield return new WaitForSeconds(Random.Range(0.03f, 0.09f));
                        Set(Random.Range(0.75f, 1.05f));
                        yield return new WaitForSeconds(Random.Range(0.04f, 0.16f));
                    }
                    nextBurst = Time.time + Random.Range(calmSeconds.x, calmSeconds.y);
                }
                yield return null;
            }
        }

        void Set(float k)
        {
            if (lamp != null) lamp.intensity = baseIntensity * k;
            if (mat != null && mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission * emissionStrength * k);
            if (buzz != null) buzz.volume = k < 0.5f ? 0.26f : 0.14f;
        }
    }
}
