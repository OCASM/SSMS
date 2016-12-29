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

        /// Prefilter threshold (gamma-encoded)
        /// Filters out pixels under this level of brightness.
        public float thresholdGamma {
            get { return Mathf.Max(_threshold, 0); }
            set { _threshold = value; }
        }

        /// Prefilter threshold (linearly-encoded)
        /// Filters out pixels under this level of brightness.
        public float thresholdLinear {
            get { return GammaToLinear(thresholdGamma); }
            set { _threshold = LinearToGamma(value); }
        }

		[HideInInspector]
        [SerializeField]
        [Tooltip("Filters out pixels under this level of brightness.")]
        float _threshold = 0f;

        /// Soft-knee coefficient
        /// Makes transition between under/over-threshold gradual.
        public float softKnee {
            get { return _softKnee; }
            set { _softKnee = value; }
        }

		[HideInInspector]
        [SerializeField, Range(0, 1)]
        [Tooltip("Makes transition between under/over-threshold gradual.")]
        float _softKnee = 0.5f;

        /// Bloom radius
        /// Changes extent of veiling effects in a screen
        /// resolution-independent fashion.
        public float radius {
            get { return _radius; }
            set { _radius = value; }
        }

		[Header("Scattering")]
        [SerializeField, Range(1, 7)]
        [Tooltip("Changes extent of veiling effects\n" +
                 "in a screen resolution-independent fashion.")]
		
        float _radius = 7f;

		/// Blur Weight
		/// Gives more strength to the blur texture during the combiner loop.
		public float blurWeight {
			get { return _blurWeight; }
			set { _blurWeight = value; }
		}

		[SerializeField, Range(0.1f, 100)]
		[Tooltip("Higher number creates a softer look but artifacts are more pronounced.")] // TODO Better description.
		float _blurWeight = 1f;

        /// Bloom intensity
        /// Blend factor of the result image.
        public float intensity {
            get { return Mathf.Max(_intensity, 0); }
            set { _intensity = value; }
        }

        [SerializeField]
        [Tooltip("Blend factor of the result image.")]
		[Range (0,1)]
        float _intensity = 1f;

        /// High quality mode
        /// Controls filter quality and buffer resolution.
        public bool highQuality {
            get { return _highQuality; }
            set { _highQuality = value; }
        }

        [SerializeField]
        [Tooltip("Controls filter quality and buffer resolution.")]
        bool _highQuality = true;

        /// Anti-flicker filter
        /// Reduces flashing noise with an additional filter.
        [SerializeField]
        [Tooltip("Reduces flashing noise with an additional filter.")]
        bool _antiFlicker = true;

        public bool antiFlicker {
            get { return _antiFlicker; }
            set { _antiFlicker = value; }
        }

		/// Distribution texture
		[SerializeField]
		[Tooltip("1D gradient. Determines how the effect fades across distance.")]
		Texture2D _fadeRamp;

		public Texture2D fadeRamp {
			get { return _fadeRamp; }
			set { _fadeRamp = value; }
		}

		/// Blur tint
		[SerializeField]
		[Tooltip("Tints the resulting blur. ")]
		Color _blurTint = Color.white; 

		public Color blurTint {
			get { return _blurTint; }
			set { _blurTint = value; }
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
        #if UNITY_5_3_OR_NEWER
            return Mathf.LinearToGammaSpace(x);
        #else
            if (x <= 0.0031308f)
                return 12.92f * x;
            else
                return 1.055f * Mathf.Pow(x, 1 / 2.4f) - 0.055f;
        #endif
        }

        float GammaToLinear(float x)
        {
        #if UNITY_5_3_OR_NEWER
            return Mathf.GammaToLinearSpace(x);
        #else
            if (x <= 0.04045f)
                return x / 12.92f;
            else
                return Mathf.Pow((x + 0.055f) / 1.055f, 2.4f);
        #endif
        }

		private Camera cam;

        #endregion

        #region MonoBehaviour Functions

        void OnEnable()
        {
            var shader = _shader ? _shader : Shader.Find("Hidden/SSMS");
            _material = new Material(shader);
            _material.hideFlags = HideFlags.DontSave;

			// SMSS
			cam = this.GetComponent<Camera> ();

			if (fadeRamp == null) {
				_fadeRamp = Resources.Load("Textures/nonLinear2", typeof(Texture2D)) as Texture2D;
			};

        }

        void OnDisable()
        {
            DestroyImmediate(_material);
        }

		// [ImageEffectOpaque]
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            var useRGBM = Application.isMobilePlatform;

            // source texture size
            var tw = source.width;
            var th = source.height;

            // halve the texture size for the low quality mode
            if (!_highQuality)
            {
                tw /= 2;
                th /= 2;
            }

            // blur buffer format
            var rtFormat = useRGBM ?
                RenderTextureFormat.Default : RenderTextureFormat.DefaultHDR;

            // determine the iteration count
            var logh = Mathf.Log(th, 2) + _radius - 8;
            var logh_i = (int)logh;
            var iterations = Mathf.Clamp(logh_i, 1, kMaxIterations);

            // update the shader properties
            var lthresh = thresholdLinear;
            _material.SetFloat("_Threshold", lthresh);

            var knee = lthresh * _softKnee + 1e-5f;
            var curve = new Vector3(lthresh - knee, knee * 2, 0.25f / knee);
            _material.SetVector("_Curve", curve);

            var pfo = !_highQuality && _antiFlicker;
            _material.SetFloat("_PrefilterOffs", pfo ? -0.5f : 0.0f);

            _material.SetFloat("_SampleScale", 0.5f + logh - logh_i);
            _material.SetFloat("_Intensity", intensity);

			_material.SetTexture ("_FadeTex", _fadeRamp);
			_material.SetFloat ("_BlurWeight", _blurWeight);
			_material.SetFloat ("_Radius", _radius);
			_material.SetColor ("_BlurTint", _blurTint);

            // prefilter pass
            var prefiltered = RenderTexture.GetTemporary(tw, th, 0, rtFormat);
            var pass = _antiFlicker ? 1 : 0;
            Graphics.Blit(source, prefiltered, _material, pass);

            // construct a mip pyramid
            var last = prefiltered;
            for (var level = 0; level < iterations; level++)
            {
                _blurBuffer1[level] = RenderTexture.GetTemporary(
                    last.width / 2, last.height / 2, 0, rtFormat
                );

                pass = (level == 0) ? (_antiFlicker ? 3 : 2) : 4;
                Graphics.Blit(last, _blurBuffer1[level], _material, pass);

                last = _blurBuffer1[level];
            }

            // upsample and combine loop
            for (var level = iterations - 2; level >= 0; level--)
            {
                var basetex = _blurBuffer1[level];
                _material.SetTexture("_BaseTex", basetex);

                _blurBuffer2[level] = RenderTexture.GetTemporary(
                    basetex.width, basetex.height, 0, rtFormat
                );

                pass = _highQuality ? 6 : 5;
                Graphics.Blit(last, _blurBuffer2[level], _material, pass);
                last = _blurBuffer2[level];
            }
				
            // finish process
            _material.SetTexture("_BaseTex", source);
            pass = _highQuality ? 8 : 7;
			Graphics.Blit(last, destination, _material, pass);
	
            // release the temporary buffers
            for (var i = 0; i < kMaxIterations; i++)
            {
                if (_blurBuffer1[i] != null)
                    RenderTexture.ReleaseTemporary(_blurBuffer1[i]);

                if (_blurBuffer2[i] != null)
                    RenderTexture.ReleaseTemporary(_blurBuffer2[i]);

                _blurBuffer1[i] = null;
                _blurBuffer2[i] = null;
            }

            RenderTexture.ReleaseTemporary(prefiltered);
        }

        #endregion
    }
}