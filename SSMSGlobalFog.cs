// Modified version of Unity's Global Fog Effect for SSMS

using UnityEngine;

namespace SSMS
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("OCASM/Image Effects/SSMS Global Fog")]
#if UNITY_5_4_OR_NEWER
    [ImageEffectAllowedInSceneView]
#endif
    public class SSMSGlobalFog : MonoBehaviour
    {
        [Tooltip("Apply distance-based fog?")]
        public bool distanceFog = true;

        [Tooltip("Exclude far plane pixels from distance-based fog? (Skybox or clear color)")]
        public bool excludeFarPixels = true;

        [Tooltip("Distance fog is based on radial distance from camera when checked")]
        public bool useRadialDistance = false;

        [Tooltip("Apply height-based fog?")]
        public bool heightFog = true;

        [Tooltip("Fog top Y coordinate")]
        public float height = 1.0f;

        [Range(0.001f, 100.0f)]
        public float heightDensity = 2.0f;

        [Tooltip("Push fog away from the camera by this amount")]
        public float startDistance = 0.0f;

        [Tooltip("Clips max fog value. Allows bright lights to shine through.")]
        [Range(0, 1)]
        public float maxDensity = 0.999f;

        [Tooltip("How much light is absorbed by the fog. Not physically correct at all.")]
        [Range(0, 100)]
        public float energyLoss = 0f;

        [Tooltip("Tints the color of this instance of Global Fog.")]
        public Color fogTint = Color.white;

        [Tooltip("Overrides global settings.")]
        public bool setGlobalSettings = false;

        public Color fogColor;
        public FogMode fogMode;
        [Range(0, 1)]
        public float fogDensity;
        public float fogStart;
        public float fogEnd;

        public Shader fogShader = null;

        private Material fogMaterial;
        private RenderTexture fogRT;
        private Camera cam;

        private void OnEnable()
        {
            fogShader = Shader.Find("Hidden/SSMS Global Fog");
            if (fogMaterial == null)
            {
                fogMaterial = new Material(fogShader) { hideFlags = HideFlags.DontSave };
            }
            cam = GetComponent<Camera>();
        }

        private void OnDisable()
        {
            if (fogRT != null)
            {
                fogRT.Release();
            }
            DestroyImmediate(fogMaterial);
        }

        [ImageEffectOpaque]
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!distanceFog && !heightFog)
            {
                Graphics.Blit(source, destination);
                return;
            }

            if (setGlobalSettings)
            {
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
            }

            if (fogRT == null || fogRT.width != source.width || fogRT.height != source.height)
            {
                fogRT = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.Default);
            }

            Vector3[] frustumCorners = new Vector3[4];
            cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1), cam.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);

            Matrix4x4 frustumCornersArray = Matrix4x4.identity;
            frustumCornersArray.SetRow(0, cam.transform.TransformVector(frustumCorners[0]));
            frustumCornersArray.SetRow(1, cam.transform.TransformVector(frustumCorners[3]));
            frustumCornersArray.SetRow(2, cam.transform.TransformVector(frustumCorners[1]));
            frustumCornersArray.SetRow(3, cam.transform.TransformVector(frustumCorners[2]));

            float FdotC = cam.transform.position.y - height;
            float paramK = (FdotC <= 0.0f ? 1.0f : 0.0f);
            float excludeDepth = (excludeFarPixels ? 1.0f : 2.0f);

            fogMaterial.SetMatrix("_FrustumCornersWS", frustumCornersArray);
            fogMaterial.SetVector("_CameraWS", cam.transform.position);
            fogMaterial.SetVector("_HeightParams", new Vector4(height, FdotC, paramK, heightDensity * 0.5f));
            fogMaterial.SetVector("_DistanceParams", new Vector4(-Mathf.Max(startDistance, 0.0f), excludeDepth, 0, 0));

            FogMode sceneMode = RenderSettings.fogMode;
            float sceneDensity = RenderSettings.fogDensity;
            float sceneStart = RenderSettings.fogStartDistance;
            float sceneEnd = RenderSettings.fogEndDistance;

            bool linear = (sceneMode == FogMode.Linear);
            float diff = linear ? sceneEnd - sceneStart : 0.0f;
            float invDiff = Mathf.Abs(diff) > 0.0001f ? 1.0f / diff : 0.0f;

            Vector4 sceneParams = new Vector4
            {
                x = sceneDensity * 1.2011224087f, // density / sqrt(ln(2)), used by Exp2 fog mode
                y = sceneDensity * 1.4426950408f, // density / ln(2), used by Exp fog mode
                z = linear ? -invDiff : 0.0f,
                w = linear ? sceneEnd * invDiff : 0.0f
            };

            fogMaterial.SetVector("_SceneFogParams", sceneParams);
            fogMaterial.SetVector("_SceneFogMode", new Vector4((int)sceneMode, useRadialDistance ? 1 : 0, 0, 0));
            fogMaterial.SetColor("_FogTint", fogTint);
            fogMaterial.SetFloat("_MaxValue", maxDensity);
            fogMaterial.SetFloat("_EnLoss", energyLoss);

            int pass = (distanceFog, heightFog) switch
            {
                (true, true) => 0,
                (true, false) => 1,
                (false, true) => 2,
                _ => 0
            };

            if (pass == 0 && fogRT != null) Graphics.Blit(source, fogRT, fogMaterial, 3);
            if (pass == 1 && fogRT != null) Graphics.Blit(source, fogRT, fogMaterial, 4);
            if (pass == 2 && fogRT != null) Graphics.Blit(source, fogRT, fogMaterial, 5);

            Graphics.Blit(source, destination, fogMaterial, pass);
            Shader.SetGlobalTexture("_FogTex", fogRT);

            if (!saveFogRT && fogRT != null)
            {
                fogRT.Release();
            }
        }
    }
}
