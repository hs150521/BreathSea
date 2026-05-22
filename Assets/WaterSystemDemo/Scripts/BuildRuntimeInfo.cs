using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class BuildRuntimeInfo : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F1;
    public bool showInfo = true;

    private string info;

    void Start()
    {
        RefreshInfo();
        Debug.Log(info);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            showInfo = !showInfo;
            RefreshInfo();
        }

        if (Time.frameCount % 60 == 0)
        {
            RefreshInfo();
        }
    }

    void RefreshInfo()
    {
        string qualityName = QualitySettings.names[QualitySettings.GetQualityLevel()];

        RenderPipelineAsset rp = QualitySettings.renderPipeline;
        string renderPipelineName = rp != null ? rp.name : "Built-in / None";

        HDRenderPipelineAsset hdrpAsset = rp as HDRenderPipelineAsset;
        string hdrpName = hdrpAsset != null ? hdrpAsset.name : "Not HDRP Asset";

        info =
            $"Runtime Info\n" +
            $"Screen: {Screen.width} x {Screen.height}\n" +
            $"Current Resolution: {Screen.currentResolution.width} x {Screen.currentResolution.height} @ {Screen.currentResolution.refreshRateRatio.value:F2} Hz\n" +
            $"Fullscreen: {Screen.fullScreen}\n" +
            $"Fullscreen Mode: {Screen.fullScreenMode}\n" +
            $"Quality Level: {qualityName}\n" +
            $"Render Pipeline: {renderPipelineName}\n" +
            $"HDRP Asset: {hdrpName}\n" +
            $"VSync Count: {QualitySettings.vSyncCount}\n" +
            $"Target FrameRate: {Application.targetFrameRate}";
    }

    void OnGUI()
    {
        if (!showInfo)
            return;

        GUI.color = Color.white;
        GUI.Box(new Rect(20, 20, 520, 230), "");
        GUI.Label(new Rect(35, 35, 500, 210), info);
    }
}