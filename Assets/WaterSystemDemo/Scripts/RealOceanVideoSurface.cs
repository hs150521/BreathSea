using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

public sealed class RealOceanVideoSurface : MonoBehaviour
{
    public Transform referenceCamera;
    public BreathToWater controller;
    public string videoFileName = "ocean-waves-kalaloch.mp4";

    VideoPlayer player;
    Material material;
    RenderTexture texture;

    void Start()
    {
        if (referenceCamera == null && Camera.main != null)
            referenceCamera = Camera.main.transform;
        if (referenceCamera == null)
        {
            enabled = false;
            return;
        }

        texture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32) { name = "Recorded Ocean Video" };
        texture.Create();
        Shader shader = Shader.Find("HDRP/Unlit");
        if (shader == null)
        {
            enabled = false;
            return;
        }

        material = new Material(shader) { name = "Recorded Ocean Surface" };
        material.SetTexture("_UnlitColorMap", texture);
        material.SetFloat("_ExposureWeight", 1f);
        material.SetTextureScale("_UnlitColorMap", Vector2.one);
        material.SetTextureOffset("_UnlitColorMap", Vector2.zero);
        GetComponent<MeshRenderer>().sharedMaterial = material;
        GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        GetComponent<MeshRenderer>().receiveShadows = false;

        player = gameObject.AddComponent<VideoPlayer>();
        player.source = VideoSource.Url;
        player.url = System.IO.Path.Combine(Application.streamingAssetsPath, videoFileName);
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = texture;
        player.isLooping = true;
        player.playOnAwake = true;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.Prepare();
    }

    void LateUpdate()
    {
        if (referenceCamera == null)
            return;

        Camera camera = referenceCamera.GetComponent<Camera>();
        if (camera == null)
            return;

        float distance = camera.nearClipPlane + 0.08f;
        transform.SetPositionAndRotation(referenceCamera.position + referenceCamera.forward * distance, referenceCamera.rotation);
        // The editor can capture at a wider aspect than the camera's Game view.
        // Deliberately overscan so the original scene can never show at an edge.
        float height = 2f * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * 1.4f;
        float targetAspect = Mathf.Max(camera.aspect, (float)Screen.width / Mathf.Max(1, Screen.height));
        transform.localScale = new Vector3(height * targetAspect, height, 1f);
        if (player != null && player.isPrepared)
        {
            float energy = controller != null ? controller.waveValue : 0f;
            // Input affects a real moving sea rather than a decorative overlay. A
            // quiet room reads as slow swell; a strong voice creates visibly faster
            // crest motion without distorting the filmed water.
            player.playbackSpeed = Mathf.Lerp(0.55f, 1.75f, energy);
        }
    }

    void OnDestroy()
    {
        if (texture != null)
            texture.Release();
    }
}
