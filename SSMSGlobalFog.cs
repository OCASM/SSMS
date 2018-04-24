// Modified version of Unity's Global Fog Effect for SSMS

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SSMS
{
	[ExecuteInEditMode]
	[RequireComponent (typeof(Camera))]
	[AddComponentMenu ("OCASM/Image Effects/SSMS Global Fog")]
	#if UNITY_5_4_OR_NEWER
	[ImageEffectAllowedInSceneView]
	#endif
	public class SSMSGlobalFog : MonoBehaviour {

		[Tooltip("Apply distance-based fog?")]
		public bool  distanceFog = true;
		[Tooltip("Exclude far plane pixels from distance-based fog? (Skybox or clear color)")]
		public bool  excludeFarPixels = true;
		[Tooltip("Distance fog is based on radial distance from camera when checked")]
		public bool  useRadialDistance = false;
		[Tooltip("Apply height-based fog?")]
		public bool  heightFog = true;
		[Tooltip("Fog top Y coordinate")]
		public float height = 1.0f;
		[Range(0.001f,100.0f)]
		public float heightDensity = 2.0f;
		[Tooltip("Push fog away from the camera by this amount")]
		public float startDistance = 0.0f;
		[Tooltip("Clips max fog value. Allows bright lights to shine through.")]
		[Range (0,1)]
		public float maxDensity = 0.999f;
		[Tooltip("How much light is absorbed by the fog. Not physically correct at all.")]
		[Range (0,100)]
		public float energyLoss = 0f;
		[Tooltip("Tints the color of this instance of Global Fog.")]
		public Color fogTint = Color.white;
		bool saveFogRT = true;
		public Shader fogShader = null;
		[Header("Global Fog Settings")]
		[Tooltip("Overrides global settings.")]
		public bool setGlobalSettings = false;
		public Color fogColor;
		public FogMode fogMode;
		[Tooltip("For exponential modes only.")]
		[Range(0,1)]
		public float fogDensity;
		[Tooltip("For linear mode only.")]
		public float fogStart;
		[Tooltip("For linear mode only.")]
		public float fogEnd;

		private Material fogMaterial = null;

		[HideInInspector]
		public RenderTexture fogRT;


		void OnEnable(){
			fogShader = Shader.Find ("Hidden/SSMS Global Fog");

			if (fogMaterial == null) {
				fogMaterial = new Material (fogShader);
				fogMaterial.hideFlags = HideFlags.DontSave;
			}

		}

		void OnDisable(){
			if (fogRT != null){
				fogRT.Release ();
			}

			DestroyImmediate (fogMaterial);
		}

		[ImageEffectOpaque]
		void OnRenderImage(RenderTexture source, RenderTexture destination)
		{	
			// Global fog settings

			if (setGlobalSettings) {
				if (fogStart < 0) { fogStart = 0; }
				if (fogEnd < 0) { fogEnd = 0; }

				RenderSettings.fogColor = this.fogColor;
				RenderSettings.fogMode = this.fogMode;
				RenderSettings.fogDensity = this.fogDensity;
				RenderSettings.fogStartDistance = this.fogStart;
				RenderSettings.fogEndDistance = this.fogEnd;
			}


			if (/*CheckResources() == false ||*/ (!distanceFog && !heightFog))
			{
				Graphics.Blit(source, destination);
				return;
			}

			//Create a new FogRT
            if (saveFogRT && (fogRT == null || fogRT.height < source.height || fogRT.width < source.width))
            {
				fogRT = new RenderTexture (source.width, source.height, 0, RenderTextureFormat.Default);
			}

			Camera cam = GetComponent<Camera>();
			Transform camtr = cam.transform;

			Vector3[] frustumCorners = new Vector3[4];
			cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1), cam.farClipPlane, cam.stereoActiveEye, frustumCorners);
			var bottomLeft = camtr.TransformVector(frustumCorners[0]);
			var topLeft = camtr.TransformVector(frustumCorners[1]);
			var topRight = camtr.TransformVector(frustumCorners[2]);
			var bottomRight = camtr.TransformVector(frustumCorners[3]);

			Matrix4x4 frustumCornersArray = Matrix4x4.identity;
			frustumCornersArray.SetRow(0, bottomLeft);
			frustumCornersArray.SetRow(1, bottomRight);
			frustumCornersArray.SetRow(2, topLeft);
			frustumCornersArray.SetRow(3, topRight);

			var camPos = camtr.position;
			float FdotC = camPos.y - height;
			float paramK = (FdotC <= 0.0f ? 1.0f : 0.0f);
			float excludeDepth = (excludeFarPixels ? 1.0f : 2.0f);
			fogMaterial.SetMatrix("_FrustumCornersWS", frustumCornersArray);
			fogMaterial.SetVector("_CameraWS", camPos);
			fogMaterial.SetVector("_HeightParams", new Vector4(height, FdotC, paramK, heightDensity * 0.5f));
			fogMaterial.SetVector("_DistanceParams", new Vector4(-Mathf.Max(startDistance, 0.0f), excludeDepth, 0, 0));

			var sceneMode = RenderSettings.fogMode;
			var sceneDensity = RenderSettings.fogDensity;
			var sceneStart = RenderSettings.fogStartDistance;
			var sceneEnd = RenderSettings.fogEndDistance;
			Vector4 sceneParams;
			bool linear = (sceneMode == FogMode.Linear);
			float diff = linear ? sceneEnd - sceneStart : 0.0f;
			float invDiff = Mathf.Abs(diff) > 0.0001f ? 1.0f / diff : 0.0f;
			sceneParams.x = sceneDensity * 1.2011224087f; // density / sqrt(ln(2)), used by Exp2 fog mode
			sceneParams.y = sceneDensity * 1.4426950408f; // density / ln(2), used by Exp fog mode
			sceneParams.z = linear ? -invDiff : 0.0f;
			sceneParams.w = linear ? sceneEnd * invDiff : 0.0f;
			fogMaterial.SetVector("_SceneFogParams", sceneParams);
			fogMaterial.SetVector("_SceneFogMode", new Vector4((int)sceneMode, useRadialDistance ? 1 : 0, 0, 0));

			fogMaterial.SetColor ("_FogTint", fogTint);
			fogMaterial.SetFloat ("_MaxValue", maxDensity);
			fogMaterial.SetFloat ("_EnLoss", energyLoss);

			int pass = 0;
			if (distanceFog && heightFog){
				pass = 0; // distance + height
				if (saveFogRT) { Graphics.Blit(source, fogRT, fogMaterial, 3); }
			}
			else if (distanceFog){
				pass = 1; // distance only
				if (saveFogRT) { Graphics.Blit(source, fogRT, fogMaterial, 4); }
			}
			else{
				pass = 2; // height only
				if (saveFogRT) { Graphics.Blit(source, fogRT, fogMaterial, 5); }
			}

			Graphics.Blit(source, destination, fogMaterial, pass);
			Shader.SetGlobalTexture ("_FogTex", fogRT);

			if (!saveFogRT && fogRT != null){
				fogRT.Release ();
			}
		}

	}
}