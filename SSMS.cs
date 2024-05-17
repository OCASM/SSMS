// Screen Space Multiple Scattering for Unity
//
// Copyright (C) 2015, 2016 Keijiro Takahashi, OCASM
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
using UnityEngine;

namespace SSMS
{	
    [ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("OCASM/Image Effects/SSMS")]
	#if UNITY_5_4_OR_NEWER
	[ImageEffectAllowedInSceneView]
	#endif
    public class SSMS : MonoBehaviour
    {
        #region Public Properties

        public float thresholdGamma {
            get => Mathf.Max(_threshold, 0);
            set => _threshold = value;
        }

        public float thresholdLinear {
            get => GammaToLinear(thresholdGamma);
            set => _threshold = LinearToGamma(value);
        }

		[HideInInspector, SerializeField, Tooltip("Filters out pixels under this level of brightness.")]
        float _threshold = 0f;

        public float softKnee {
            get => _softKnee;
            set => _softKnee = value;
        }

		[HideInInspector, SerializeField, Range(0, 1), Tooltip("Makes transition between under/over-threshold gradual.")]
        float _softKnee = 0.5f;

        public float radius {
            get => _radius;
            set => _radius = value;
        }

		[Header("Scattering"), SerializeField, Range(1, 7), Tooltip("Changes extent of veiling effects\nin a screen resolution-independent fashion.")]
        float _radius = 7f;

		public float blurWeight {
			get => _blurWeight;
			set => _blurWeight = value;
		}

		[SerializeField, Range(0.1f, 100), Tooltip("Higher number creates a softer look but artifacts are more pronounced.")]
		float _blurWeight = 1f;

        public float intensity {
            get => Mathf.Max(_intensity, 0);
            set => _intensity = value;
        }

        [SerializeField, Tooltip("Blend factor of the result image."), Range(0, 1)]
        float _intensity = 1f;

        public bool highQuality {
            get => _highQuality;
            set => _highQuality = value;
        }

        [SerializeField, Tooltip("Controls filter quality and buffer resolution.")]
        bool _highQuality = true;

        public bool antiFlicker {
            get => _antiFlicker;
            set => _antiFlicker = value;
        }

        [SerializeField, Tooltip("Reduces flashing noise with an additional filter.")]
        bool _antiFlicker = true;

		[SerializeField, Tooltip("1D gradient. Determines how the effect fades across distance.")]
		Texture2D _fadeRamp;

		public Texture2D fadeRamp {
			get => _fadeRamp;
			set => _fadeRamp = value;
		}

		[SerializeField, Tooltip("Tints the resulting blur.")]
		Color _blurTint = Color.white;

		public Color blurTint {
			get => _blurTint;
			set => _blurTint = value;
		}

		#endregion

        #region Private Members

        [SerializeField, HideInInspector]
        Shader _shader;

        Material _material;

        const int kMaxIterations = 16;
        RenderTexture[] _blurBuffer1 = new RenderTexture[kMaxIterations];
        RenderTexture[] _blurBuffer2 = new RenderTexture[kMaxIterations];

        float LinearToGamma(float x)
        {
            return Mathf.LinearToGammaSpace(x);
        }

        float GammaToLinear(float x)
        {
            return Mathf.GammaToLinearSpace(x);
        }

        #endregion

        #region MonoBehaviour Functions

        void OnEnable()
        {
            _shader ??= Shader.Find("Hidden/SSMS");
            _material = new Material(_shader) { hideFlags = HideFlags.DontSave };

			if (_fadeRamp == null) {
				_fadeRamp = Resources.Load<Texture2D>("Textures/nonLinear2");
			}
        }

        void OnDisable()
        {
            DestroyImmediate(_material);
        }

		void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            var useRGBM = Application.isMobilePlatform;

            int tw = source.width;
            int th = source.height;

            if (!_highQuality)
            {
                tw /= 2;
                th /= 2;
            }

            RenderTextureFormat rtFormat = useRGBM ? RenderTextureFormat.Default : RenderTextureFormat.DefaultHDR;

            int iterations = Mathf.Clamp((int)(Mathf.Log(th, 2) + _radius - 8), 1, kMaxIterations);

            _material.SetFloat("_Threshold", thresholdLinear);

            float knee = thresholdLinear * _softKnee + 1e-5f;
            _material.SetVector("_Curve", new Vector3(thresholdLinear - knee, knee * 2, 0.25f / knee));

            _material.SetFloat("_PrefilterOffs", (!_highQuality && _antiFlicker) ? -0.5f : 0.0f);
            _material.SetFloat("_SampleScale", 0.5f + Mathf.Log(th, 2) - (int)Mathf.Log(th, 2));
            _material.SetFloat("_Intensity", intensity);
			_material.SetTexture("_FadeTex", _fadeRamp);
			_material.SetFloat("_BlurWeight", _blurWeight);
			_material.SetFloat("_Radius", _radius);
			_material.SetColor("_BlurTint", _blurTint);

            RenderTexture prefiltered = RenderTexture.GetTemporary(tw, th, 0, rtFormat);
            Graphics.Blit(source, prefiltered, _material, _antiFlicker ? 1 : 0);

            RenderTexture last = prefiltered;
            for (int level = 0; level < iterations; level++)
            {
                _blurBuffer1[level] = RenderTexture.GetTemporary(last.width / 2, last.height / 2, 0, rtFormat);
                Graphics.Blit(last, _blurBuffer1[level], _material, level == 0 ? (_antiFlicker ? 3 : 2) : 4);
                last = _blurBuffer1[level];
            }

            for (int level = iterations - 2; level >= 0; level--)
            {
                RenderTexture basetex = _blurBuffer1[level];
                _material.SetTexture("_BaseTex", basetex);

                _blurBuffer2[level] = RenderTexture.GetTemporary(basetex.width, basetex.height, 0, rtFormat);
                Graphics.Blit(last, _blurBuffer2[level], _material, _highQuality ? 6 : 5);
                last = _blurBuffer2[level];
            }

            _material.SetTexture("_BaseTex", source);
            Graphics.Blit(last, destination, _material, _highQuality ? 8 : 7);

            for (int i = 0; i < kMaxIterations; i++)
            {
                if (_blurBuffer1[i] != null) RenderTexture.ReleaseTemporary(_blurBuffer1[i]);
                if (_blurBuffer2[i] != null) RenderTexture.ReleaseTemporary(_blurBuffer2[i]);
                _blurBuffer1[i] = null;
                _blurBuffer2[i] = null;
            }

            RenderTexture.ReleaseTemporary(prefiltered);
        }

        #endregion
    }
}

