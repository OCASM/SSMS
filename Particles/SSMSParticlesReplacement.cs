using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class SSMSParticlesReplacement : MonoBehaviour {

	public Shader replacementShader;

	// Use this for initialization
	void OnEnable () {
		if (replacementShader != null) {
			GetComponent<Camera> ().SetReplacementShader (replacementShader, "RenderType");
		}
	}
	
	// Update is called once per frame
	void OnDisable () {
		GetComponent<Camera> ().ResetReplacementShader ();
	}
}
