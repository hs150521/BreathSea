using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Generates a moving, multi-direction water normal field on the GPU. It keeps
/// fine-scale detail independent from the low-frequency surface displacement.
/// </summary>
[DefaultExecutionOrder(15)]
public sealed class GpuOceanSpectrum : MonoBehaviour
{
    const int Resolution = 512;

    public Transform referenceCamera;
    [Range(0f, 1f)] public float audioEnergy;
    public RenderTexture normalFoamTexture { get; private set; }

    ComputeShader spectrumShader;
    int kernel;

    public void Configure(Transform cameraTransform)
    {
        referenceCamera = cameraTransform;
        EnsureResources();
    }

    void OnEnable()
    {
        EnsureResources();
    }

    void LateUpdate()
    {
        if (spectrumShader == null || normalFoamTexture == null)
            return;

        Vector3 cameraPosition = referenceCamera != null ? referenceCamera.position : Vector3.zero;
        spectrumShader.SetInt("_Resolution", Resolution);
        spectrumShader.SetFloat("_TimeValue", Time.time);
        spectrumShader.SetFloat("_Energy", audioEnergy);
        spectrumShader.SetVector("_WorldOffset", new Vector4(cameraPosition.x, cameraPosition.z, 0f, 0f));
        spectrumShader.SetTexture(kernel, "_NormalFoam", normalFoamTexture);
        spectrumShader.Dispatch(kernel, Resolution / 8, Resolution / 8, 1);
    }

    void EnsureResources()
    {
        if (spectrumShader == null)
        {
            spectrumShader = Resources.Load<ComputeShader>("GpuOceanSpectrum");
            if (spectrumShader == null)
            {
                Debug.LogError("GpuOceanSpectrum: compute shader is missing.");
                enabled = false;
                return;
            }
            kernel = spectrumShader.FindKernel("CSMain");
        }

        if (normalFoamTexture != null)
            return;

        normalFoamTexture = new RenderTexture(Resolution, Resolution, 0)
        {
            name = "Exhibition Ocean Spectrum",
            graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8
        };
        normalFoamTexture.Create();
    }

    void OnDestroy()
    {
        if (normalFoamTexture == null)
            return;
        normalFoamTexture.Release();
        Destroy(normalFoamTexture);
    }
}
