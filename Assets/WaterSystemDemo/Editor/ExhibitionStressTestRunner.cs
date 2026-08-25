using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ExhibitionStressTestRunner
{
    const string SessionKey = "BreathSea.ExhibitionStressTest";
    static readonly string RequestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "run-exhibition-stress-test.flag");
    static double testStartTime;
    static double previousStepTime;
    static int testStartFrame;
    static bool capturedQuiet;
    static bool capturedShortPeak;
    static bool capturedSustainedInput;

    static ExhibitionStressTestRunner()
    {
        EditorApplication.update += CheckForExternalRequest;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("BreathSea/Run Exhibition Stress Test")]
    public static void RunStressTest()
    {
        SessionState.SetBool(SessionKey, true);

        if (EditorApplication.isPlaying)
        {
            BeginStressTest();
            return;
        }

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.EnterPlaymode();
    }

    static void CheckForExternalRequest()
    {
        if (!File.Exists(RequestPath))
            return;

        File.Delete(RequestPath);
        RunStressTest();
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(SessionKey, false))
            BeginStressTest();
    }

    static void BeginStressTest()
    {
        if (!SessionState.GetBool(SessionKey, false))
            return;

        // A paused Editor still accepts Camera.Render(), which used to produce
        // misleading zero-input captures. This runner owns the temporary play
        // session, so it can explicitly resume before sampling the simulator.
        EditorApplication.isPaused = false;

        BreathToWater controller = Object.FindFirstObjectByType<BreathToWater>();
        if (controller == null)
        {
            Debug.LogError("Exhibition stress test could not find BreathToWater in the active scene.");
            SessionState.SetBool(SessionKey, false);
            EditorApplication.ExitPlaymode();
            return;
        }

        controller.StartStressTest();
        controller.showRuntimePanel = false;
        testStartTime = EditorApplication.timeSinceStartup;
        previousStepTime = testStartTime;
        testStartFrame = Time.frameCount;
        capturedQuiet = false;
        capturedShortPeak = false;
        capturedSustainedInput = false;
        EditorApplication.update += UpdateStressTest;
        Debug.Log("Exhibition stress test started with simulated audio input.");
    }

    static void UpdateStressTest()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= UpdateStressTest;
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - testStartTime;
        BreathToWater controller = Object.FindFirstObjectByType<BreathToWater>();
        if (controller != null)
        {
            float deltaTime = (float)(EditorApplication.timeSinceStartup - previousStepTime);
            controller.AdvanceStressTest((float)elapsed, Mathf.Clamp(deltaTime, 0.001f, 0.1f));
        }
        previousStepTime = EditorApplication.timeSinceStartup;
        if (!capturedQuiet && elapsed >= 0.8)
        {
            CaptureStressFrame("WaterStressTest-Quiet.png");
            capturedQuiet = true;
        }
        if (!capturedShortPeak && elapsed >= 2.0)
        {
            CaptureStressFrame("WaterStressTest-ShortPeak.png");
            capturedShortPeak = true;
        }

        if (!capturedSustainedInput && elapsed >= 7.0)
        {
            CaptureStressFrame("WaterStressTest-Sustained.png");
            capturedSustainedInput = true;
        }

        if (elapsed < 10.0)
            return;

        double duration = Mathf.Max(0.001f, (float)(EditorApplication.timeSinceStartup - testStartTime));
        float averageFps = (Time.frameCount - testStartFrame) / (float)duration;
        Debug.Log(string.Format("Exhibition stress test completed. Average game FPS: {0:F1}. Screenshots saved in Temp.", averageFps));
        SessionState.SetBool(SessionKey, false);
        EditorApplication.update -= UpdateStressTest;
        EditorApplication.ExitPlaymode();
    }

    static void CaptureStressFrame(string fileName)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            BreathToWater controller = Object.FindFirstObjectByType<BreathToWater>();
            if (controller != null && controller.referenceCamera != null)
                camera = controller.referenceCamera.GetComponent<Camera>();
        }

        if (camera == null)
            camera = Object.FindFirstObjectByType<Camera>();

        if (camera == null)
        {
            Debug.LogError("Exhibition stress test could not find the active camera for capture.");
            return;
        }

        const int width = 1280;
        const int height = 720;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);

        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(Path.Combine("Temp", fileName), image.EncodeToPNG());
            BreathToWater controller = Object.FindFirstObjectByType<BreathToWater>();
            GpuOceanSpectrum spectrum = Object.FindFirstObjectByType<GpuOceanSpectrum>();
            if (controller != null)
                Debug.Log(string.Format(
                    "Stress capture {0}: input={1:F4}, wave={2:F3}, delayed={3:F3}, gpu={4:F3}",
                    fileName, controller.rawMicLevel, controller.waveValue, controller.delayedGlobalWave,
                    spectrum != null ? spectrum.audioEnergy : -1f));
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(image);
        }
    }
}
