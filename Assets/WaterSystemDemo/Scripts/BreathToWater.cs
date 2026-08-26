#pragma warning disable 0618

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;

public class BreathToWater : MonoBehaviour
{
    public enum InputSource { Microphone, Simulator }
    public enum SimulationPattern { QuietRoom, ShortLoudBurst, SustainedVoice, ExhibitionStressTest }

    const string PreferencesPrefix = "BreathSea.Exhibition.";

    [Header("Target Water")]
    public WaterSurface water;
    [Header("Camera / Direction")]
    public Transform referenceCamera;

    [Header("Traveling Pulse Deformer")]
    [Tooltip("Legacy WaterDeformer assets are deprecated in Unity 6. Keep disabled for the exhibition ripple method.")]
    public bool useLegacyWaterDeformer = false;
    public WaterDeformer pulseDeformerTemplate;
    public int pulsePoolSize = 3;
    public float pulseNearDistance = 20f;
    public float pulseFarDistance = 150f;
    public float pulseTravelTime = 10f;
    public float pulseAmplitude = 0.45f;
    public float maxPulseAmplitude = 0.5f;
    public float pulseWidth = 118f;
    public float pulseLength = 108f;
    public float pulseSideOffset = 0f;

    [Header("Native HDRP Audio Wave Decals")]
    [Tooltip("Each sound carries a small, non-uniform foam crest group across the water without retuning the entire FFT ocean.")]
    public bool useNativeAudioWaveDecals = false;
    [Range(1, 6)] public int nativeAudioWavePoolSize = 4;
    [Tooltip("The peak displacement of a loud microphone-triggered crest group, in metres.")]
    public float nativeWaveAmplitude = 8f;
    public float nativeWaveLifetime = 4.2f;
    public float nativeWaveNearDistance = 10f;
    public float nativeWaveFarDistance = 36f;
    public float nativeWaveWidth = 15f;
    public float nativeWaveLength = 13f;

    [Header("HDRP Current Variation")]
    [Tooltip("Adds slow, spatially varied flow to the same HDRP ocean spectrum. It never deforms the shoreline height field.")]
    public bool useSpatialCurrentVariation = true;

    [Header("Procedural Near Ocean")]
    [Tooltip("A finite Gerstner mesh gives the exhibition camera real multi-directional near-water displacement.")]
    public bool useProceduralNearOcean = false;
    [Range(48, 112)] public int proceduralOceanResolution = 80;
    public float proceduralOceanWidth = 150f;
    public float proceduralOceanLength = 145f;
    public float proceduralOceanForwardOffset = 82f;
    public float proceduralOceanBaseLift = 0.42f;

    [Header("Continuous Ocean Replacement")]
    [Tooltip("Uses one camera-following continuous ocean mesh instead of the periodic HDRP water surface.")]
    public bool useContinuousOceanReplacement = false;

    [Header("Recorded Ocean Fallback")]
    [Tooltip("Uses licensed real ocean footage for the exhibition display while microphone energy changes its motion rate.")]
    public bool useRecordedOceanFallback = false;

    [Header("Shoreline Clearance")]
    [Tooltip("Raises the water datum enough that the deepest exhibition trough cannot reveal submerged shore rocks.")]
    [Range(0f, 0.5f)] public float waterDatumLift = 0.32f;


    [Header("Audio Input")]
    public InputSource inputSource = InputSource.Microphone;
    public string microphoneName = "";
    public int sampleRate = 44100;
    public int sampleWindow = 1024;
    public int recordingLengthSeconds = 10;

    [Header("Audio Calibration")]
    [Tooltip("Room noise measured with the exhibition microphone.")]
    public float noiseFloor = 0.004f;
    [Tooltip("Input level that produces the maximum wave response.")]
    public float breathCeiling = 0.022f;
    [Tooltip("Additional sensitivity after calibration.")]
    public float breathGain = 1f;
    [Tooltip("Mixes short peaks into the RMS level so a short loud sound is not lost.")]
    [Range(0f, 1f)] public float peakInfluence = 0.45f;

    [Header("Sound Response")]
    public float attackSpeed = 10f;
    public float releaseSpeed = 1.1f;
    public float responseCurve = 1.5f;

    [Header("Pulse Trigger")]
    [Range(0f, 1f)] public float pulseThreshold = 0.32f;
    public float pulseCooldown = 0.75f;
    public float triggerPulseStrength = 1f;

    [Header("Slow Swell")]
    public float slowSwellStrength = 0.12f;
    public float slowSwellMinSpeed = 0.06f;
    public float slowSwellMaxSpeed = 0.18f;

    [Header("Global Ocean Response")]
    [Tooltip("Large-scale sea state changes gradually; immediate response is handled by local wave groups.")]
    public float globalAttackSpeed = 0.32f;
    public float globalReleaseSpeed = 0.09f;
    [Range(0f, 1f)] public float delayedGlobalWave;

    [Header("Water Safety")]
    [Tooltip("Upper displacement multiplier for the broad HDRP swell. Values above 1 are intentionally theatrical.")]
    [Range(0.1f, 1.5f)] public float maximumLargeBandMultiplier = 1.25f;
    [Range(0f, 1f)] public float rippleInfluence = 1f;
    [Range(0f, 1f)] public float foamInfluence = 0.75f;

    [Tooltip("Keeps all shoreline decals inside the HDRP Water Decal Atlas.")]
    public bool limitWaterDecalResolution = true;
    public Vector2Int maximumWaterDecalResolution = new Vector2Int(64, 64);
    [Tooltip("Prevents shoreline decals from pulling the water below nearby rock geometry.")]
    public bool limitWaterDecalDepression = true;
    [Tooltip("Disables old static shoreline deformation decals that carve artificial holes into the water.")]
    public bool disableStaticSceneDeformation = true;
    [Range(0f, 1f)] public float maximumWaterDecalDepression = 0.15f;
    [Tooltip("Positive lift cap for local audio wave decals, in metres.")]
    [Range(0f, 10f)] public float maximumWaterDecalLift = 9f;

    [Header("Exhibition Water Look")]
    [Tooltip("Applies a less directional, less mirror-like ocean treatment at runtime.")]
    public bool applyExhibitionWaterLook = true;
    [Range(0f, 360f)] public float largeWaveDirection = 224f;
    [Range(0f, 360f)] public float rippleDirection = 318f;
    [Range(0f, 1f)] public float nearWaterSmoothness = 0.82f;
    [Range(0f, 1f)] public float farWaterSmoothness = 0.56f;
    [Tooltip("Retained for scene compatibility. The exhibition controller never changes the authored camera transform.")]
    public float exhibitionCameraHeightOffset = 1.35f;
    public float exhibitionCameraPitchDown = 3.2f;

    [Header("Calm Ocean")]
    public float calmDistantWindSpeed = 28f;
    public float calmChaos = 0.72f;
    public float calmFirstBand = 0.62f;
    public float calmSecondBand = 0.35f;
    public float calmRipplesWindSpeed = 1.8f;
    public float calmRipplesChaos = 0.34f;
    public float calmTimeMultiplier = 0.75f;
    public float calmFoam = 0.25f;

    [Header("Active Ocean")]
    public float activeDistantWindSpeed = 56f;
    public float activeChaos = 0.78f;
    public float activeFirstBand = 0.82f;
    public float activeSecondBand = 0.56f;
    public float activeRipplesWindSpeed = 3.8f;
    public float activeRipplesChaos = 0.42f;
    public float activeTimeMultiplier = 1.15f;
    public float activeFoam = 0.55f;

    [Header("Exhibition Control Panel")]
    public KeyCode controlPanelKey = KeyCode.F8;
    public KeyCode simulatorStressTestKey = KeyCode.F9;
    public bool showRuntimePanel;
    public SimulationPattern simulationPattern = SimulationPattern.ShortLoudBurst;
    [Range(0f, 0.1f)] public float simulatedManualLevel = 0.003f;

    [Header("Debug")]
    public KeyCode testPulseKey = KeyCode.T;
    [Range(0f, 1f)] public float breathValue;
    [Range(0f, 1f)] public float waveValue;
    [Range(0f, 1f)] public float pulseValue;
    [Range(0f, 1f)] public float slowSwellValue;
    public float rawMicLevel;
    public float peakMicLevel;
    public float maxRawMicLevel;

    AudioClip micClip;
    float[] samples;
    float smoothedSound;
    float smoothedGlobalWave;
    float slowSwellPhase;
    float simulatorStartTime;
    float lastPulseTime = -999f;
    int pulseSequence;
    string statusMessage = "Starting...";
    PulseSlot[] pulseSlots;
    NativeWaveSlot[] nativeWaveSlots;
    Material nativeWaveMaterial;
    WaterDecal[] currentFlowDecals;
    Material currentFlowMaterial;
    WaterDecal spectralOceanDeformer;
    Material spectralOceanDeformerMaterial;
    Texture2D spectralOceanHeightField;
    Texture2D[] nativeWavePackets;
    const string WavePacketTextureProperty = "_SampleTexture2D_b6ca83ee7a5744eda121f52ddeb1fa1d_Texture_1_Texture2D";
    GameObject proceduralOceanObject;
    Mesh proceduralOceanMesh;
    Material proceduralOceanMaterial;
    Vector3[] proceduralVertices;
    Vector3[] proceduralNormals;
    Vector2[] proceduralCoordinates;
    ContinuousOceanClipmap continuousOcean;
    GpuOceanSpectrum gpuOceanSpectrum;
    Material exhibitionWaterMaterial;

    static readonly Vector2[] GerstnerDirections =
    {
        new Vector2(0.82f, 0.57f), new Vector2(-0.42f, 0.91f), new Vector2(0.97f, -0.24f),
        new Vector2(-0.76f, 0.65f), new Vector2(0.18f, -0.98f), new Vector2(0.58f, -0.81f)
    };
    static readonly float[] GerstnerLengths = { 34f, 21f, 13f, 8.5f, 5.2f, 3.4f };
    static readonly float[] GerstnerAmplitudes = { 0.46f, 0.28f, 0.16f, 0.095f, 0.052f, 0.025f };
    static readonly float[] GerstnerPhases = { 0.2f, 1.7f, 3.1f, 4.4f, 5.2f, 2.6f };

    class PulseSlot
    {
        public WaterDeformer deformer;
        public float age;
        public float strength;
        public Vector3 start;
        public Vector3 end;
        public bool active;
    }

    class NativeWaveSlot
    {
        public WaterDecal decal;
        public Material material;
        public float age;
        public float duration;
        public float strength;
        public float scale;
        public int textureIndex;
        public Vector3 start;
        public Vector3 end;
        public bool active;
    }


    void Start()
    {
        // The exhibition can briefly lose foreground focus when the operator uses
        // the control panel. Keep microphone response and the automated stress run
        // advancing rather than capturing a frozen frame.
        Application.runInBackground = true;

        if (water == null)
        {
            Debug.LogError("BreathToWater: Water Surface is not assigned.");
            enabled = false;
            return;
        }

        if (referenceCamera == null && Camera.main != null)
            referenceCamera = Camera.main.transform;

        if (referenceCamera == null)
        {
            Debug.LogError("BreathToWater: Reference Camera is not assigned.");
            enabled = false;
            return;
        }

        ApplyLegacySafetyDefaults();
        LoadRuntimeSettings();
        LiftWaterDatumForShoreline();
        ApplyExhibitionWaterLook();
        ConfigureWaterDecals();
        SetupNativeAudioWaveDecals();
        SetupSpatialCurrentVariation();
        SetupProceduralNearOcean();
        SetupContinuousOceanReplacement();
        SetupRecordedOceanFallback();
        samples = new float[Mathf.Max(64, sampleWindow)];
        SetupMicrophone();
        SetupPulseDeformers();
    }

    void Update()
    {
        if (Input.GetKeyDown(controlPanelKey))
            showRuntimePanel = !showRuntimePanel;

        if (Input.GetKeyDown(simulatorStressTestKey))
            StartStressTest();

        if (Input.GetKeyDown(testPulseKey))
            StartTravelingPulse(1f);

        float inputLevel = GetAudioLevel(out float inputPeak);
        ApplyInputLevel(inputLevel, inputPeak, Time.deltaTime, true);
    }

    void ApplyInputLevel(float inputLevel, float inputPeak, float deltaTime, bool updateLocalEffects)
    {
        rawMicLevel = inputLevel;
        peakMicLevel = inputPeak;
        maxRawMicLevel = Mathf.Max(maxRawMicLevel, rawMicLevel);

        float normalized = Mathf.InverseLerp(noiseFloor, Mathf.Max(noiseFloor + 0.0001f, breathCeiling), rawMicLevel);
        normalized = Mathf.Clamp01(normalized * breathGain);

        float envelopeSpeed = normalized > smoothedSound ? attackSpeed : releaseSpeed;
        smoothedSound = Mathf.Lerp(smoothedSound, normalized, 1f - Mathf.Exp(-envelopeSpeed * Mathf.Max(0.0001f, deltaTime)));
        breathValue = smoothedSound;

        float baseWave = Mathf.Clamp01(Mathf.Pow(breathValue, Mathf.Max(0.05f, responseCurve)));
        waveValue = baseWave;

        if (updateLocalEffects)
        {
            TryTriggerTravelingPulse(baseWave);
            UpdateTravelingPulses();
            UpdateNativeAudioWaveDecals();
            UpdateSlowSwell(delayedGlobalWave);
            UpdateSpectralOceanDeformer(delayedGlobalWave);
            UpdateProceduralNearOcean(delayedGlobalWave);
        }

        float globalSpeed = baseWave > smoothedGlobalWave ? globalAttackSpeed : globalReleaseSpeed;
        smoothedGlobalWave = Mathf.Lerp(smoothedGlobalWave, baseWave, 1f - Mathf.Exp(-globalSpeed * Mathf.Max(0.0001f, deltaTime)));
        delayedGlobalWave = smoothedGlobalWave;
        ApplyGlobalWater(delayedGlobalWave);
        if (continuousOcean != null)
            continuousOcean.audioEnergy = delayedGlobalWave;
    }

    public void AdvanceStressTest(float elapsedSeconds, float deltaTime)
    {
        float input;
        if (elapsedSeconds < 1.5f) input = 0.003f;
        else if (elapsedSeconds < 1.8f) input = 0.055f;
        else if (elapsedSeconds < 3.5f) input = 0.012f + Mathf.Sin(elapsedSeconds * 12f) * 0.003f;
        else if (elapsedSeconds < 4.2f) input = 0.07f;
        else if (elapsedSeconds < 6.5f) input = 0.004f;
        else input = 0.028f;
        ApplyInputLevel(input, input, deltaTime, false);
    }

    public void SelectMicrophone(string deviceName)
    {
        if (deviceName == microphoneName && micClip != null && Microphone.IsRecording(microphoneName))
            return;

        StopMicrophone();
        microphoneName = deviceName;
        inputSource = InputSource.Microphone;
        SetupMicrophone();
        PlayerPrefs.SetString(PreferencesPrefix + "Microphone", microphoneName);
        PlayerPrefs.Save();
    }

    public void StartStressTest()
    {
        StopMicrophone();
        inputSource = InputSource.Simulator;
        simulationPattern = SimulationPattern.ExhibitionStressTest;
        simulatorStartTime = Time.time;
        statusMessage = "Simulator: ExhibitionStressTest";
    }

    void SetupMicrophone()
    {
        if (inputSource != InputSource.Microphone)
            return;

        string[] devices = Microphone.devices;
        if (devices.Length == 0)
        {
            statusMessage = "No microphone detected. Select Simulator with F8 to test.";
            Debug.LogWarning("BreathToWater: No microphone detected.");
            return;
        }

        bool selectedDeviceExists = false;
        foreach (string device in devices)
        {
            if (device == microphoneName)
                selectedDeviceExists = true;
        }

        if (!selectedDeviceExists)
            microphoneName = devices[0];

        micClip = Microphone.Start(microphoneName, true, recordingLengthSeconds, sampleRate);
        statusMessage = "Microphone: " + microphoneName;
        Debug.Log("BreathToWater started microphone: " + microphoneName);
    }

    void ConfigureWaterDecals()
    {
        if (!limitWaterDecalResolution)
            return;

        Vector2Int atlasSafeResolution = new Vector2Int(
            Mathf.Min(64, maximumWaterDecalResolution.x),
            Mathf.Min(64, maximumWaterDecalResolution.y)
        );
        WaterDecal[] decals = FindObjectsByType<WaterDecal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int adjustedCount = 0;
        int deformationCount = 0;
        for (int i = 0; i < decals.Length; i++)
        {
            Vector2Int resolution = decals[i].resolution;
            if (resolution.x > atlasSafeResolution.x || resolution.y > atlasSafeResolution.y)
            {
                decals[i].resolution = atlasSafeResolution;
                decals[i].RequestUpdate();
                adjustedCount++;
            }

            if (!limitWaterDecalDepression)
                continue;

            if (disableStaticSceneDeformation && !decals[i].transform.IsChildOf(transform))
            {
                if (!Mathf.Approximately(decals[i].amplitude, 0f))
                {
                    decals[i].amplitude = 0f;
                    decals[i].RequestUpdate();
                    deformationCount++;
                }
                continue;
            }

            float safeAmplitude = Mathf.Clamp(decals[i].amplitude, -maximumWaterDecalDepression, maximumWaterDecalLift);
            if (Mathf.Approximately(safeAmplitude, decals[i].amplitude))
                continue;

            decals[i].amplitude = safeAmplitude;
            decals[i].RequestUpdate();
            deformationCount++;
        }

        Debug.Log("BreathToWater: Water Decal Atlas limited " + adjustedCount + " of " + decals.Length + " decals to " + atlasSafeResolution.x + "x" + atlasSafeResolution.y + "; disabled or limited deformation on " + deformationCount + " decals.");
    }

    void ApplyExhibitionWaterLook()
    {
        if (!applyExhibitionWaterLook)
            return;

        // The authored scene predates the exhibition preset, so enforce its safe response
        // here instead of depending on serialized inspector defaults.
        pulseNearDistance = 20f;
        pulseTravelTime = 10f;
        pulseAmplitude = 0.45f;
        maxPulseAmplitude = 0.5f;
        pulseWidth = 118f;
        pulseLength = 108f;
        useNativeAudioWaveDecals = true;
        useProceduralNearOcean = false;
        // Keep one coherent HDRP spectrum. The former normal-map, static deformation,
        // and current-decal overlays each had an independent periodic domain; their
        // interference was the source of the visible blocks and evenly spaced bands.
        useRecordedOceanFallback = false;
        useContinuousOceanReplacement = false;
        useSpatialCurrentVariation = true;
        // The copied graph has no authored inputs of its own. Let HDRP use its native
        // Water material so the reflection and micro-normal paths remain coupled to
        // the active simulation instead of to a duplicate graph asset.
        water.customMaterial = null;
        // Loud inputs remain visibly strong, but leave clearance below the fixed camera.
        responseCurve = Mathf.Max(responseCurve, 1.5f);
        nativeWaveAmplitude = Mathf.Max(nativeWaveAmplitude, 8f);
        maximumWaterDecalLift = Mathf.Max(maximumWaterDecalLift, 9f);
        nativeWaveNearDistance = 10f;
        nativeWaveFarDistance = 36f;
        nativeWaveWidth = 42f;
        nativeWaveLength = 34f;
        globalAttackSpeed = 0.32f;
        globalReleaseSpeed = 0.09f;
        maximumLargeBandMultiplier = Mathf.Min(Mathf.Max(maximumLargeBandMultiplier, 1.1f), 1.3f);
        calmDistantWindSpeed = 28f;
        calmFirstBand = 0.62f;
        calmSecondBand = 0.35f;
        activeDistantWindSpeed = 56f;
        activeFirstBand = 0.82f;
        activeSecondBand = 0.56f;
        activeFoam = Mathf.Max(activeFoam, 0.55f);

        // Create the ocean spectrum once. HDRP invalidates and rebuilds its FFT whenever
        // these values change, so they must never be driven directly by microphone input.
        // Keep the three HDRP bands in distinct jobs.  The swell is directional
        // enough to read as a body of water, the middle band gives it cross-seas,
        // and the ripples only resolve close to the viewer.
        water.repetitionSize = 1850f;
        water.largeOrientationValue = largeWaveDirection;
        // The exhibition is a sheltered sunset view, not an open-water gale. A
        // lower spectral peak avoids resolving into repeated cross-screen crests at
        // the fixed camera while high chaos keeps the remaining waves directionally
        // spread instead of forming one parade of parallel bands.
        water.largeWindSpeed = 34f;
        water.largeChaos = 0.92f;
        water.largeBand0FadeMode = WaterSurface.FadeMode.Custom;
        water.largeBand0FadeStart = 560f;
        water.largeBand0FadeDistance = 1280f;
        water.largeBand1FadeMode = WaterSurface.FadeMode.Custom;
        // The middle band is useful near the shore but becomes a row of parallel
        // horizon stripes at this fixed low camera angle. Fade it before it reaches
        // the far field; the large band then carries the distant sea state alone.
        water.largeBand1FadeStart = 160f;
        water.largeBand1FadeDistance = 420f;
        water.ripples = true;
        water.ripplesMotionMode = WaterPropertyOverrideMode.Custom;
        water.ripplesOrientationValue = rippleDirection;
        water.ripplesWindSpeed = 5.2f;
        water.ripplesChaos = 0.96f;
        water.ripplesFadeMode = WaterSurface.FadeMode.Custom;
        water.ripplesFadeStart = 38f;
        water.ripplesFadeDistance = 210f;
        rippleInfluence = Mathf.Min(rippleInfluence, 0.7f);

        // Runtime audio decals are spawned relative to the viewing camera. Keep HDRP's
        // finite decal simulation region centred on that camera so they reach the water.
        water.decalRegionAnchor = referenceCamera;
        water.decalRegionSize = new Vector2(240f, 240f);

        HDAdditionalCameraData cameraData = referenceCamera.GetComponent<HDAdditionalCameraData>();
        if (cameraData != null)
            cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        // Broad highlights need roughness variation in this low-sun composition.
        water.startSmoothness = 0.74f;
        water.endSmoothness = 0.50f;
        water.smoothnessFadeStart = 35f;
        water.smoothnessFadeDistance = 350f;

        water.foamColor = new Color(0.86f, 0.9f, 0.88f, 1f);
        water.foamSmoothness = 0.2f;
        water.foamTextureTiling = 0.35f;

        // Keep shallow foreground rock detail from reading as black exposed geometry.
        water.maxRefractionDistance = 1.5f;
        water.absorptionDistance = 3.25f;
        water.refractionColor = new Color(0.015f, 0.095f, 0.12f, 1f);
        water.scatteringColor = new Color(0.075f, 0.27f, 0.3f, 1f);
        water.ambientScattering = 0.22f;
        water.heightScattering = 0.38f;
        water.displacementScattering = 0.5f;
        water.maxTessellationFactor = Mathf.Max(water.maxTessellationFactor, 6);
    }

    void SetupSpectralOceanDeformer()
    {
        Material template = Resources.Load<Material>("AudioPulseDecal/Water Circle Deformer");
        if (template == null)
        {
            Debug.LogError("BreathToWater: Spectral ocean deformer material is missing.");
            return;
        }

        spectralOceanHeightField = BuildSpectralHeightField(256);
        spectralOceanDeformerMaterial = new Material(template) { name = "Exhibition Spectral Ocean Deformer" };
        spectralOceanDeformerMaterial.EnableKeyword("_AFFECTS_DEFORMATION");
        spectralOceanDeformerMaterial.SetTexture(WavePacketTextureProperty, spectralOceanHeightField);

        GameObject obj = new GameObject("Exhibition Spectral Ocean Deformer");
        obj.transform.SetParent(transform, false);
        obj.transform.position = new Vector3(referenceCamera.position.x, water.transform.position.y, referenceCamera.position.z + 620f);
        WaterDecal decal = obj.AddComponent<WaterDecal>();
        decal.material = spectralOceanDeformerMaterial;
        decal.regionSize = new Vector2(1800f, 1600f);
        decal.resolution = new Vector2Int(256, 256);
        decal.updateMode = CustomRenderTextureUpdateMode.OnLoad;
        decal.amplitude = 0.12f;
        decal.surfaceFoamDimmer = 0f;
        decal.deepFoamDimmer = 0f;
        decal.RequestUpdate();
        spectralOceanDeformer = decal;
    }

    void SetupGpuOceanDetail()
    {
        Material template = Resources.Load<Material>("RainOcean/Shader Graphs_RainOcean");
        if (template == null)
        {
            Debug.LogError("BreathToWater: GPU ocean water graph is missing.");
            return;
        }

        gpuOceanSpectrum = GetComponent<GpuOceanSpectrum>();
        if (gpuOceanSpectrum == null)
            gpuOceanSpectrum = gameObject.AddComponent<GpuOceanSpectrum>();
        gpuOceanSpectrum.Configure(referenceCamera);

        exhibitionWaterMaterial = new Material(template) { name = "Exhibition Multi-Direction Water" };
        exhibitionWaterMaterial.SetTexture("_Droplets_Texture", gpuOceanSpectrum.normalFoamTexture);
        exhibitionWaterMaterial.SetFloat("_Normal_Intensity", 0.68f);
        exhibitionWaterMaterial.SetFloat("_Tiling", 0.9f);
        water.customMaterial = exhibitionWaterMaterial;
    }

    Texture2D BuildSpectralHeightField(int resolution)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
        {
            name = "Exhibition Spectral Height Field",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[resolution * resolution];
        Vector2[] directions =
        {
            new Vector2(0.90f, 0.43f), new Vector2(0.56f, 0.83f), new Vector2(-0.30f, 0.95f),
            new Vector2(-0.86f, 0.51f), new Vector2(0.98f, -0.21f), new Vector2(0.32f, -0.95f),
            new Vector2(-0.61f, -0.79f), new Vector2(0.12f, 0.99f)
        };
        float[] wavelengths = { 420f, 285f, 190f, 126f, 82f, 54f, 33f, 21f };
        float[] weights = { 0.45f, 0.29f, 0.19f, 0.12f, 0.075f, 0.048f, 0.028f, 0.016f };
        float[] phases = { 0.2f, 1.7f, 3.5f, 5.1f, 2.4f, 4.3f, 0.9f, 5.8f };

        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            Vector2 position = new Vector2((x / (float)(resolution - 1) - 0.5f) * 1800f, (y / (float)(resolution - 1) - 0.5f) * 1600f);
            float height = 0f;
            for (int wave = 0; wave < directions.Length; wave++)
            {
                float phase = Vector2.Dot(position, directions[wave]) * (Mathf.PI * 2f / wavelengths[wave]) + phases[wave];
                height += weights[wave] * Mathf.Sin(phase);
            }

            // A soft border prevents the finite deformation region from reading as a rectangle.
            float edgeX = Mathf.SmoothStep(0f, 0.06f, x / (float)(resolution - 1)) * (1f - Mathf.SmoothStep(0.94f, 1f, x / (float)(resolution - 1)));
            float edgeY = Mathf.SmoothStep(0f, 0.06f, y / (float)(resolution - 1)) * (1f - Mathf.SmoothStep(0.94f, 1f, y / (float)(resolution - 1)));
            float normalized = Mathf.Clamp01(0.5f + height * 0.5f * edgeX * edgeY);
            pixels[y * resolution + x] = new Color(normalized, normalized, normalized, 1f);
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    void UpdateSpectralOceanDeformer(float seaState)
    {
        if (spectralOceanDeformer == null)
            return;

        spectralOceanDeformer.amplitude = Mathf.Lerp(0.08f, 0.22f, Mathf.SmoothStep(0f, 1f, seaState));
    }

    void LiftWaterDatumForShoreline()
    {
        if (waterDatumLift <= 0f)
            return;

        Vector3 position = water.transform.position;
        position.y += waterDatumLift;
        water.transform.position = position;
    }

    // The production scene is distributed separately from this repository. Detect its
    // former high-energy preset so an older downloaded scene gets exhibition-safe values.
    void ApplyLegacySafetyDefaults()
    {
        bool isLegacyPreset = activeDistantWindSpeed > 100f ||
                              activeFirstBand > 1f ||
                              activeSecondBand > 1f ||
                              breathGain > 3f ||
                              (activeDistantWindSpeed <= 45f && maximumLargeBandMultiplier <= 0.78f);

        if (!isLegacyPreset)
            return;

        pulseNearDistance = 20f;
        pulseTravelTime = 10f;
        pulseAmplitude = 0.45f;
        maxPulseAmplitude = 0.5f;
        pulseWidth = 118f;
        pulseLength = 108f;
        breathGain = 1f;
        peakInfluence = 0.45f;
        attackSpeed = 10f;
        releaseSpeed = 1.1f;
        responseCurve = 0.75f;
        pulseThreshold = 0.32f;
        pulseCooldown = 0.75f;
        slowSwellStrength = 0.12f;
        globalAttackSpeed = 4f;
        globalReleaseSpeed = 0.5f;
        maximumLargeBandMultiplier = 0.84f;
        rippleInfluence = 1f;
        foamInfluence = 0.75f;
        calmDistantWindSpeed = 28f;
        calmChaos = 0.72f;
        calmFirstBand = 0.62f;
        calmSecondBand = 0.35f;
        calmRipplesWindSpeed = 1.8f;
        calmRipplesChaos = 0.34f;
        activeDistantWindSpeed = 56f;
        activeChaos = 0.78f;
        activeFirstBand = 0.82f;
        activeSecondBand = 0.56f;
        activeRipplesWindSpeed = 3.8f;
        activeRipplesChaos = 0.42f;
        Debug.Log("BreathToWater: Applied exhibition-safe settings to a legacy scene preset.");
    }

    void StopMicrophone()
    {
        if (!string.IsNullOrEmpty(microphoneName) && Microphone.IsRecording(microphoneName))
            Microphone.End(microphoneName);

        micClip = null;
    }

    float GetAudioLevel(out float peak)
    {
        peak = 0f;
        if (inputSource == InputSource.Simulator)
        {
            float simulated = GetSimulatedAudioLevel();
            peak = simulated;
            statusMessage = "Simulator: " + simulationPattern;
            return simulated;
        }

        if (micClip == null || !Microphone.IsRecording(microphoneName))
        {
            statusMessage = "Microphone unavailable";
            return 0f;
        }

        int micPosition = Microphone.GetPosition(microphoneName);
        if (micPosition < 0)
            return 0f;

        int startPosition = micPosition - samples.Length;
        if (startPosition < 0)
            startPosition += micClip.samples;

        micClip.GetData(samples, startPosition);
        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float sample = samples[i];
            sum += sample * sample;
            peak = Mathf.Max(peak, Mathf.Abs(sample));
        }

        float rms = Mathf.Sqrt(sum / samples.Length);
        return Mathf.Lerp(rms, peak, peakInfluence);
    }

    float GetSimulatedAudioLevel()
    {
        float cycle;
        switch (simulationPattern)
        {
            case SimulationPattern.QuietRoom:
                return simulatedManualLevel;
            case SimulationPattern.SustainedVoice:
                return 0.016f + Mathf.Sin(Time.time * 7f) * 0.004f;
            case SimulationPattern.ExhibitionStressTest:
                cycle = Mathf.Repeat(Time.time - simulatorStartTime, 8f);
                if (cycle < 1.5f) return 0.003f;
                if (cycle < 1.8f) return 0.055f;
                if (cycle < 3.5f) return 0.012f + Mathf.Sin(Time.time * 12f) * 0.003f;
                if (cycle < 4.2f) return 0.07f;
                if (cycle < 6.5f) return 0.004f;
                return 0.028f;
            default:
                cycle = Mathf.Repeat(Time.time, 4f);
                return cycle > 1.25f && cycle < 1.55f ? 0.06f : 0.003f;
        }
    }

    void SetupPulseDeformers()
    {
        if (!useLegacyWaterDeformer)
        {
            if (pulseDeformerTemplate != null)
                pulseDeformerTemplate.gameObject.SetActive(false);
            pulseSlots = null;
            return;
        }

        if (pulseDeformerTemplate == null)
        {
            Debug.LogWarning("BreathToWater: No pulse deformer is assigned.");
            return;
        }

        pulseSlots = new PulseSlot[Mathf.Max(1, pulsePoolSize)];
        pulseDeformerTemplate.amplitude = 0f;
        pulseDeformerTemplate.gameObject.SetActive(false);

        for (int i = 0; i < pulseSlots.Length; i++)
        {
            GameObject obj = Instantiate(pulseDeformerTemplate.gameObject, pulseDeformerTemplate.transform.parent);
            obj.name = "Sound Traveling Pulse " + i;
            obj.SetActive(false);
            pulseSlots[i] = new PulseSlot { deformer = obj.GetComponent<WaterDeformer>() };
        }
    }

    void SetupNativeAudioWaveDecals()
    {
        if (!useNativeAudioWaveDecals)
            return;

        nativeWaveMaterial = Resources.Load<Material>("AudioPulseDecal/Audio Foam Wave");
        if (nativeWaveMaterial == null)
        {
            Debug.LogError("BreathToWater: Audio pulse decal material is missing. Re-import the HDRP water decal samples.");
            useNativeAudioWaveDecals = false;
            return;
        }

        nativeWaveSlots = new NativeWaveSlot[Mathf.Max(1, nativeAudioWavePoolSize)];
        nativeWavePackets = new Texture2D[4];
        for (int i = 0; i < nativeWavePackets.Length; i++)
            nativeWavePackets[i] = Resources.Load<Texture2D>("AudioPulseDecal/Packets/WavePacketV2_" + i);
        for (int i = 0; i < nativeWaveSlots.Length; i++)
        {
            GameObject obj = new GameObject("Audio Water Decal " + i);
            obj.transform.SetParent(transform, false);
            WaterDecal decal = obj.AddComponent<WaterDecal>();
            Material slotMaterial = new Material(nativeWaveMaterial) { name = "Audio Wave Packet " + i };
            // HDRP's sample graph has its own height limiter. Keep it in step with
            // the exhibition control rather than silently clamping a loud crest.
            slotMaterial.SetFloat("_Max_Amplitude", maximumWaterDecalLift);
            decal.material = slotMaterial;
            decal.resolution = new Vector2Int(64, 64);
            decal.updateMode = CustomRenderTextureUpdateMode.Realtime;
            decal.surfaceFoamDimmer = 0f;
            decal.deepFoamDimmer = 0f;
            decal.amplitude = 0f;
            obj.SetActive(false);
            nativeWaveSlots[i] = new NativeWaveSlot { decal = decal, material = slotMaterial };
            Debug.Log("BreathToWater: Audio packet " + i + " uses " + slotMaterial.shader.name + ".");
        }
    }

    void SetupSpatialCurrentVariation()
    {
        if (!useSpatialCurrentVariation)
            return;

        currentFlowMaterial = Resources.Load<Material>("AudioPulseDecal/Audio Current Flow");
        if (currentFlowMaterial == null)
        {
            Debug.LogError("BreathToWater: Audio current flow material is missing.");
            useSpatialCurrentVariation = false;
            return;
        }

        // One continuous field bends both large FFT bands across the entire visible
        // sea. It avoids the obvious boundaries and conflicting directions created
        // by several small overlapping current rectangles.
        water.supportLargeCurrent = true;
        water.largeCurrentRes = WaterSurface.WaterDecalRegionResolution.Resolution256;
        water.supportRipplesCurrent = false;
        water.decalRegionSize = new Vector2(960f, 720f);
        currentFlowDecals = new WaterDecal[1];
        Vector3 forward = referenceCamera.forward;
        forward.y = 0f;
        forward.Normalize();
        GameObject obj = new GameObject("Exhibition Continuous Ocean Current");
        obj.transform.SetParent(transform, false);
        obj.transform.position = referenceCamera.position + forward * 310f;
        obj.transform.position = new Vector3(obj.transform.position.x, water.transform.position.y, obj.transform.position.z);
        obj.transform.rotation = Quaternion.Euler(0f, referenceCamera.eulerAngles.y + 18f, 0f);

        WaterDecal decal = obj.AddComponent<WaterDecal>();
        decal.material = currentFlowMaterial;
        decal.regionSize = new Vector2(920f, 680f);
        decal.resolution = new Vector2Int(128, 128);
        decal.updateMode = CustomRenderTextureUpdateMode.OnLoad;
        decal.amplitude = 0f;
        decal.surfaceFoamDimmer = 0f;
        decal.deepFoamDimmer = 0f;
        decal.RequestUpdate();
        currentFlowDecals[0] = decal;
        Debug.Log("BreathToWater: Enabled one continuous HDRP large-wave current field.");
    }


    void UpdateDelayedGlobalWave(float targetWave)
    {
        float speed = targetWave > smoothedGlobalWave ? globalAttackSpeed : globalReleaseSpeed;
        smoothedGlobalWave = Mathf.Lerp(smoothedGlobalWave, targetWave, 1f - Mathf.Exp(-speed * Time.deltaTime));
        delayedGlobalWave = smoothedGlobalWave;
    }

    void TryTriggerTravelingPulse(float baseWave)
    {
        if (baseWave < pulseThreshold || Time.time - lastPulseTime < pulseCooldown)
            return;

        StartTravelingPulse(Mathf.Clamp01(baseWave * triggerPulseStrength));
        lastPulseTime = Time.time;
    }

    void StartTravelingPulse(float strength)
    {
        Vector3 forward = referenceCamera.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            return;
        forward.Normalize();

        Vector3 right = referenceCamera.right;
        right.y = 0f;
        right.Normalize();

        float waterY = pulseDeformerTemplate != null ? pulseDeformerTemplate.transform.position.y : water.transform.position.y;
        pulseSequence++;
        float lateralVariation = Mathf.Lerp(-22f, 22f, Mathf.PerlinNoise(pulseSequence * 1.37f, 0.23f));
        float sizeVariation = Mathf.Lerp(0.78f, 1.28f, Mathf.PerlinNoise(pulseSequence * 2.11f, 0.71f));
        Vector3 origin = referenceCamera.position + right * (pulseSideOffset + lateralVariation);

        if (useNativeAudioWaveDecals && nativeWaveSlots != null)
        {
            StartNativeAudioWave(origin, forward, sizeVariation, strength);
            return;
        }

        if (!useLegacyWaterDeformer)
            return;

        if (pulseSlots == null || pulseSlots.Length == 0)
            return;

        PulseSlot slot = pulseSlots[0];
        for (int i = 0; i < pulseSlots.Length; i++)
        {
            if (!pulseSlots[i].active)
            {
                slot = pulseSlots[i];
                break;
            }
        }
        slot.start = origin + forward * pulseNearDistance;
        slot.end = origin + forward * pulseFarDistance;
        slot.start.y = waterY;
        slot.end.y = waterY;
        slot.age = 0f;
        slot.strength = Mathf.Max(0.12f, strength * 0.65f);
        slot.active = true;

        slot.deformer.gameObject.SetActive(true);
        slot.deformer.amplitude = 0f;
        slot.deformer.regionSize = new Vector2(pulseWidth * sizeVariation, pulseLength * sizeVariation);
        slot.deformer.boxBlend = new Vector2(slot.deformer.regionSize.x * 0.92f, slot.deformer.regionSize.y * 0.92f);
        slot.deformer.cubicBlend = true;
        slot.deformer.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        slot.deformer.transform.position = slot.start;
    }

    void StartNativeAudioWave(Vector3 origin, Vector3 forward, float sizeVariation, float strength)
    {
        NativeWaveSlot slot = nativeWaveSlots[0];
        for (int i = 0; i < nativeWaveSlots.Length; i++)
        {
            if (!nativeWaveSlots[i].active)
            {
                slot = nativeWaveSlots[i];
                break;
            }
        }

        float waterY = water.transform.position.y;
        slot.age = 0f;
        slot.duration = nativeWaveLifetime * Mathf.Lerp(0.82f, 1.2f, sizeVariation);
        slot.strength = Mathf.Clamp01(strength);
        slot.scale = sizeVariation;
        slot.textureIndex = pulseSequence % nativeWavePackets.Length;
        // Each event is an independently phased, bounded wave group. It travels as a
        // group; its texture contains several oblique wavelengths rather than a ring.
        float lateralJitter = Mathf.Lerp(-18f, 18f, Mathf.PerlinNoise(pulseSequence * 3.17f, 0.41f));
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        // Keep theatrical crests in the near field where their vertical motion reads
        // clearly at the authored fixed camera angle.
        // Place the tall part of the crest in the middle distance. A high wave there
        // reads strongly against the horizon without physically intersecting the lens.
        float forwardDistance = Mathf.Lerp(48f, 72f, Mathf.PerlinNoise(pulseSequence * 2.63f, 0.87f));
        slot.start = origin + forward * forwardDistance + right * lateralJitter;
        slot.end = slot.start + forward * Mathf.Lerp(10f, 24f, sizeVariation);
        slot.start.y = waterY;
        slot.end.y = waterY;
        slot.active = true;

        slot.decal.transform.position = slot.start;
        slot.decal.transform.rotation = Quaternion.Euler(0f, pulseSequence * 47.3f, 0f);
        slot.decal.regionSize = new Vector2(nativeWaveWidth * sizeVariation, nativeWaveLength * sizeVariation);
        slot.decal.amplitude = 0f;
        if (nativeWavePackets[slot.textureIndex] != null)
        {
            Camera activeCamera = referenceCamera.GetComponent<Camera>();
            Vector3 viewport = activeCamera != null ? activeCamera.WorldToViewportPoint(slot.start) : Vector3.zero;
            Debug.Log("BreathToWater: Started audio packet " + slot.textureIndex + " at " + slot.start + ", viewport " + viewport + ".");
        }
        else
            Debug.LogError("BreathToWater: Audio packet texture " + slot.textureIndex + " could not be loaded.");
        slot.decal.RequestUpdate();
        slot.decal.gameObject.SetActive(true);
    }

    void UpdateTravelingPulses()
    {
        pulseValue = 0f;
        if (pulseSlots == null)
            return;

        for (int i = 0; i < pulseSlots.Length; i++)
        {
            PulseSlot slot = pulseSlots[i];
            if (!slot.active)
                continue;

            slot.age += Time.deltaTime;
            float t = slot.age / Mathf.Max(0.01f, pulseTravelTime);
            if (t >= 1f)
            {
                slot.deformer.amplitude = 0f;
                slot.deformer.gameObject.SetActive(false);
                slot.active = false;
                continue;
            }

            float moveT = Mathf.SmoothStep(0f, 1f, t);
            float fadeIn = Mathf.SmoothStep(0f, 0.1f, t);
            float fadeOut = 1f - Mathf.SmoothStep(0.85f, 1f, t);
            float envelope = fadeIn * fadeOut;
            float farBoost = Mathf.Lerp(1f, 1.15f, moveT);
            slot.deformer.transform.position = Vector3.Lerp(slot.start, slot.end, moveT);
            slot.deformer.amplitude = Mathf.Min(maxPulseAmplitude, pulseAmplitude * slot.strength * envelope * farBoost);
            slot.deformer.regionSize *= Mathf.Lerp(1f, 1.12f, Time.deltaTime);
            slot.deformer.boxBlend = new Vector2(slot.deformer.regionSize.x * 0.92f, slot.deformer.regionSize.y * 0.92f);
            pulseValue = Mathf.Max(pulseValue, envelope * slot.strength);
        }
    }

    void UpdateNativeAudioWaveDecals()
    {
        if (nativeWaveSlots == null)
            return;

        // Never allow a runtime F8 adjustment or a persisted value to lift a crest
        // through the authored camera. The safety margin is intentionally zero here
        // so the loud state can use the full available vertical range.
        float cameraLiftLimit = GetCameraLiftLimit();

        for (int i = 0; i < nativeWaveSlots.Length; i++)
        {
            NativeWaveSlot slot = nativeWaveSlots[i];
            if (!slot.active)
                continue;

            slot.age += Time.deltaTime;
            float t = slot.age / Mathf.Max(0.01f, slot.duration);
            if (t >= 1f)
            {
                slot.decal.amplitude = 0f;
                slot.decal.RequestUpdate();
                slot.decal.gameObject.SetActive(false);
                slot.active = false;
                continue;
            }

            float envelope = Mathf.SmoothStep(0f, 0.18f, t) * (1f - Mathf.SmoothStep(0.72f, 1f, t));
            float travel = Mathf.SmoothStep(0f, 1f, t);
            slot.decal.surfaceFoamDimmer = Mathf.Clamp01(slot.strength * envelope * 0.58f);
            slot.decal.deepFoamDimmer = 0f;
            // Positive-only displacement preserves the raised shoreline clearance:
            // a loud input adds a finite crest group rather than carving a trough
            // that can expose the rocks under the authored water level.
            float distanceFromCamera = Vector3.Distance(referenceCamera.position, slot.decal.transform.position);
            float distanceBlend = Mathf.InverseLerp(18f, 55f, distanceFromCamera);
            float spatialLiftLimit = Mathf.Lerp(cameraLiftLimit, maximumWaterDecalLift, distanceBlend);
            slot.decal.amplitude = Mathf.Min(
                spatialLiftLimit,
                nativeWaveAmplitude * slot.strength * envelope * (0.9f + 0.1f * Mathf.Sin(slot.age * 5.1f)));
            slot.material.SetFloat("_Max_Amplitude", maximumWaterDecalLift);
            slot.decal.transform.position = Vector3.Lerp(slot.start, slot.end, travel);
            float breathing = 1f + 0.07f * Mathf.Sin((slot.age + slot.textureIndex) * 2.3f);
            slot.decal.regionSize = new Vector2(nativeWaveWidth * slot.scale * breathing, nativeWaveLength * slot.scale * breathing);
            slot.decal.RequestUpdate();
            pulseValue = Mathf.Max(pulseValue, envelope * slot.strength);
        }
    }

    float GetCameraLiftLimit()
    {
        if (referenceCamera == null || water == null)
            return maximumWaterDecalLift;

        float clearance = referenceCamera.position.y - water.transform.position.y;
        return Mathf.Max(0.2f, clearance);
    }

    void UpdateSlowSwell(float baseWave)
    {
        float swellSpeed = Mathf.Lerp(slowSwellMinSpeed, slowSwellMaxSpeed, baseWave);
        slowSwellPhase += swellSpeed * Mathf.PI * 2f * Time.deltaTime;
        slowSwellValue = (Mathf.Sin(slowSwellPhase) * 0.5f + 0.5f) * slowSwellStrength * baseWave;
    }

    void ApplyGlobalWater(float delayedWave)
    {
        // Do not change wind speed, chaos, orientation, repetition size, or ripple speed
        // here. Any of those invalidates HDRP's FFT and recreates the surface every frame.
        // Loud voices must read immediately. The delayed envelope remains the tail
        // after a shout, while the live envelope raises the full ocean on its attack.
        float seaState = Mathf.SmoothStep(0f, 1f, Mathf.Max(delayedWave, waveValue));
        // Keep the largest wavelength below the point where a low exhibition camera
        // turns it into an evenly spaced stripe pattern. Audio energy remains clear
        // in the middle band, foam, and changing reflection instead.
        float safeGlobalCap = Mathf.Min(maximumLargeBandMultiplier, 1.3f);
        water.largeBand0Multiplier = Mathf.Lerp(0.30f, safeGlobalCap, seaState);
        water.largeBand1Multiplier = Mathf.Lerp(0.18f, 0.95f, seaState);
        water.timeMultiplier = 0.86f;
        water.simulationFoamAmount = Mathf.Lerp(0.16f, 0.48f, Mathf.Clamp01(seaState * foamInfluence));
        if (continuousOcean != null)
            continuousOcean.audioEnergy = seaState;
        if (gpuOceanSpectrum != null)
            gpuOceanSpectrum.audioEnergy = seaState;
    }

    void SetupProceduralNearOcean()
    {
        if (!useProceduralNearOcean || referenceCamera == null)
            return;

        Shader shader = Shader.Find("HDRP/Lit");
        if (shader == null)
        {
            Debug.LogError("BreathToWater: HDRP/Lit is unavailable for the near-ocean mesh.");
            useProceduralNearOcean = false;
            return;
        }

        int resolution = Mathf.Clamp(proceduralOceanResolution, 48, 112);
        int vertexCount = (resolution + 1) * (resolution + 1);
        proceduralVertices = new Vector3[vertexCount];
        proceduralNormals = new Vector3[vertexCount];
        proceduralCoordinates = new Vector2[vertexCount];
        int[] triangles = new int[resolution * resolution * 6];

        int vertex = 0;
        for (int z = 0; z <= resolution; z++)
        for (int x = 0; x <= resolution; x++)
        {
            proceduralCoordinates[vertex] = new Vector2(x / (float)resolution - 0.5f, z / (float)resolution - 0.5f);
            proceduralNormals[vertex] = Vector3.up;
            vertex++;
        }

        int triangle = 0;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            int a = z * (resolution + 1) + x;
            int b = a + 1;
            int c = a + resolution + 1;
            int d = c + 1;
            triangles[triangle++] = a;
            triangles[triangle++] = c;
            triangles[triangle++] = b;
            triangles[triangle++] = b;
            triangles[triangle++] = c;
            triangles[triangle++] = d;
        }

        proceduralOceanObject = new GameObject("Exhibition Gerstner Near Ocean");
        proceduralOceanObject.transform.SetParent(transform, false);
        proceduralOceanMesh = new Mesh { name = "Exhibition Gerstner Near Ocean Mesh" };
        proceduralOceanMesh.indexFormat = IndexFormat.UInt32;
        proceduralOceanMesh.vertices = proceduralVertices;
        proceduralOceanMesh.normals = proceduralNormals;
        proceduralOceanMesh.uv = proceduralCoordinates;
        proceduralOceanMesh.triangles = triangles;
        proceduralOceanMesh.MarkDynamic();

        MeshFilter filter = proceduralOceanObject.AddComponent<MeshFilter>();
        filter.sharedMesh = proceduralOceanMesh;
        MeshRenderer renderer = proceduralOceanObject.AddComponent<MeshRenderer>();
        proceduralOceanMaterial = new Material(shader) { name = "Exhibition Gerstner Near Ocean Material" };
        proceduralOceanMaterial.SetColor("_BaseColor", new Color(0.02f, 0.075f, 0.10f, 1f));
        proceduralOceanMaterial.SetFloat("_Metallic", 0.12f);
        proceduralOceanMaterial.SetFloat("_Smoothness", 0.9f);
        renderer.sharedMaterial = proceduralOceanMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void SetupContinuousOceanReplacement()
    {
        if (!useContinuousOceanReplacement)
            return;

        // This renderer owns the full visible water field. Keep older experiments
        // disabled so there cannot be a foreground/background water seam.
        useProceduralNearOcean = false;
        useNativeAudioWaveDecals = false;
        continuousOcean = GetComponent<ContinuousOceanClipmap>();
        if (continuousOcean == null)
            continuousOcean = gameObject.AddComponent<ContinuousOceanClipmap>();
        continuousOcean.useHdrpWaterRenderer = false;
        continuousOcean.Configure(referenceCamera, water);
        continuousOcean.enabled = true;
    }

    void SetupRecordedOceanFallback()
    {
        if (!useRecordedOceanFallback)
            return;

        useContinuousOceanReplacement = false;
        if (continuousOcean != null)
            continuousOcean.enabled = false;

        GameObject videoSurface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        videoSurface.name = "Exhibition Recorded Ocean";
        RealOceanVideoSurface recordedOcean = videoSurface.AddComponent<RealOceanVideoSurface>();
        recordedOcean.videoFileName = "kuriats-ocean-surface.mp4";
        recordedOcean.referenceCamera = referenceCamera;
        recordedOcean.controller = this;
        water.enabled = false;
    }

    void UpdateProceduralNearOcean(float seaState)
    {
        if (proceduralOceanMesh == null || referenceCamera == null)
            return;

        Vector3 forward = referenceCamera.forward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 centre = referenceCamera.position + forward * proceduralOceanForwardOffset;
        centre.y = water.transform.position.y + proceduralOceanBaseLift;
        proceduralOceanObject.transform.position = centre;
        proceduralOceanObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        float energy = Mathf.Lerp(0.72f, 1.06f, Mathf.SmoothStep(0f, 1f, seaState));
        float time = Time.time * Mathf.Lerp(0.78f, 0.95f, seaState);
        for (int i = 0; i < proceduralVertices.Length; i++)
        {
            Vector2 coordinate = proceduralCoordinates[i];
            Vector2 local = new Vector2(coordinate.x * proceduralOceanWidth, coordinate.y * proceduralOceanLength);
            Vector3 position = new Vector3(local.x, 0f, local.y);
            Vector3 normal = Vector3.up;

            for (int wave = 0; wave < GerstnerDirections.Length; wave++)
            {
                Vector2 direction = GerstnerDirections[wave];
                float wavelength = GerstnerLengths[wave];
                float amplitude = GerstnerAmplitudes[wave] * energy;
                float waveNumber = Mathf.PI * 2f / wavelength;
                float phase = waveNumber * Vector2.Dot(direction, local) + Mathf.Sqrt(9.81f * waveNumber) * time + GerstnerPhases[wave];
                float steepness = Mathf.Min(0.52f, 0.28f / (wave + 1));
                float horizontal = steepness * amplitude * Mathf.Cos(phase);
                position.x += direction.x * horizontal;
                position.z += direction.y * horizontal;
                position.y += amplitude * Mathf.Sin(phase);
                float derivative = amplitude * waveNumber * Mathf.Cos(phase);
                normal.x -= direction.x * derivative;
                normal.z -= direction.y * derivative;
            }

            proceduralVertices[i] = position;
            proceduralNormals[i] = normal.normalized;
        }

        proceduralOceanMesh.vertices = proceduralVertices;
        proceduralOceanMesh.normals = proceduralNormals;
        proceduralOceanMesh.RecalculateBounds();
    }

    void OnGUI()
    {
        if (!showRuntimePanel)
            return;

        float panelWidth = Mathf.Min(560f, Screen.width - 24f);
        float panelHeight = Mathf.Min(700f, Screen.height - 24f);
        Rect panel = new Rect(12f, 12f, panelWidth, panelHeight);
        GUI.Box(panel, "Exhibition Audio Controls (F8)");
        GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 28f, panel.width - 28f, panel.height - 42f));

        GUILayout.Label(statusMessage);
        GUILayout.Label(string.Format("Input level RMS/peak mix: {0:F4}  Peak: {1:F4}  Wave: {2:P0}", rawMicLevel, peakMicLevel, waveValue));
        DrawLevelBar(rawMicLevel, Mathf.Max(0.01f, breathCeiling));
        GUILayout.Space(8f);
        GUILayout.Label("Input source");
        InputSource newSource = (InputSource)GUILayout.SelectionGrid((int)inputSource, new[] { "Microphone", "Simulator" }, 2);
        if (newSource != inputSource)
        {
            inputSource = newSource;
            if (inputSource == InputSource.Microphone) SetupMicrophone(); else StopMicrophone();
        }

        if (inputSource == InputSource.Microphone)
        {
            string[] devices = Microphone.devices;
            if (devices.Length == 0) GUILayout.Label("No microphone devices are currently available.");
            foreach (string device in devices)
                if (GUILayout.Button(device + (device == microphoneName ? " (selected)" : ""))) SelectMicrophone(device);
        }
        else
        {
            simulationPattern = (SimulationPattern)GUILayout.SelectionGrid((int)simulationPattern, new[] { "Quiet", "Short burst", "Sustained", "Stress test" }, 2);
            if (simulationPattern == SimulationPattern.QuietRoom)
                simulatedManualLevel = DrawSlider("Manual level", simulatedManualLevel, 0f, 0.1f);
        }

        GUILayout.Space(8f);
        GUILayout.Label("Calibration and response");
        noiseFloor = DrawSlider("Noise floor", noiseFloor, 0f, 0.05f);
        breathCeiling = DrawSlider("Maximum input", breathCeiling, 0.001f, 0.1f);
        breathGain = DrawSlider("Sensitivity", breathGain, 0.25f, 4f);
        peakInfluence = DrawSlider("Peak response", peakInfluence, 0f, 1f);
        attackSpeed = DrawSlider("Rise speed", attackSpeed, 1f, 20f);
        releaseSpeed = DrawSlider("Return speed", releaseSpeed, 0.1f, 5f);
        responseCurve = DrawSlider("Wave curve", responseCurve, 0.3f, 2f);
        pulseThreshold = DrawSlider("Pulse threshold", pulseThreshold, 0.05f, 1f);
        maximumLargeBandMultiplier = DrawSlider("Wave height cap", maximumLargeBandMultiplier, 0.1f, 1.5f);
        nativeWaveAmplitude = DrawSlider("Loud crest amplitude", nativeWaveAmplitude, 0f, 2f);
        maximumWaterDecalLift = DrawSlider("Crest lift cap", maximumWaterDecalLift, 0f, 2f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save on this computer")) SaveRuntimeSettings();
        if (GUILayout.Button("Reset peak meter")) maxRawMicLevel = 0f;
        GUILayout.EndHorizontal();
        GUILayout.Label("Saved settings are restored whenever the exhibition build starts.");
        GUILayout.EndArea();
    }

    float DrawSlider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(130f));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(160f));
        GUILayout.Label(value.ToString("F3"), GUILayout.Width(52f));
        GUILayout.EndHorizontal();
        return value;
    }

    void DrawLevelBar(float value, float maximum)
    {
        Rect rect = GUILayoutUtility.GetRect(1f, 18f);
        GUI.Box(rect, GUIContent.none);
        float width = Mathf.Clamp01(value / maximum) * (rect.width - 4f);
        GUI.Box(new Rect(rect.x + 2f, rect.y + 2f, width, rect.height - 4f), GUIContent.none);
    }

    void SaveRuntimeSettings()
    {
        PlayerPrefs.SetString(PreferencesPrefix + "Microphone", microphoneName);
        PlayerPrefs.SetFloat(PreferencesPrefix + "NoiseFloor", noiseFloor);
        PlayerPrefs.SetFloat(PreferencesPrefix + "Ceiling", breathCeiling);
        PlayerPrefs.SetFloat(PreferencesPrefix + "Gain", breathGain);
        PlayerPrefs.SetFloat(PreferencesPrefix + "PeakInfluence", peakInfluence);
        PlayerPrefs.SetFloat(PreferencesPrefix + "Attack", attackSpeed);
        PlayerPrefs.SetFloat(PreferencesPrefix + "Release", releaseSpeed);
        PlayerPrefs.SetFloat(PreferencesPrefix + "Curve", responseCurve);
        PlayerPrefs.SetFloat(PreferencesPrefix + "PulseThreshold", pulseThreshold);
        PlayerPrefs.SetFloat(PreferencesPrefix + "SafetyCap", maximumLargeBandMultiplier);
        PlayerPrefs.SetFloat(PreferencesPrefix + "NativeWaveAmplitude", nativeWaveAmplitude);
        PlayerPrefs.SetFloat(PreferencesPrefix + "WaterDecalLift", maximumWaterDecalLift);
        PlayerPrefs.Save();
        statusMessage = "Settings saved.";
    }

    void LoadRuntimeSettings()
    {
        microphoneName = PlayerPrefs.GetString(PreferencesPrefix + "Microphone", microphoneName);
        noiseFloor = PlayerPrefs.GetFloat(PreferencesPrefix + "NoiseFloor", noiseFloor);
        breathCeiling = PlayerPrefs.GetFloat(PreferencesPrefix + "Ceiling", breathCeiling);
        breathGain = PlayerPrefs.GetFloat(PreferencesPrefix + "Gain", breathGain);
        peakInfluence = PlayerPrefs.GetFloat(PreferencesPrefix + "PeakInfluence", peakInfluence);
        attackSpeed = PlayerPrefs.GetFloat(PreferencesPrefix + "Attack", attackSpeed);
        releaseSpeed = PlayerPrefs.GetFloat(PreferencesPrefix + "Release", releaseSpeed);
        responseCurve = PlayerPrefs.GetFloat(PreferencesPrefix + "Curve", responseCurve);
        pulseThreshold = PlayerPrefs.GetFloat(PreferencesPrefix + "PulseThreshold", pulseThreshold);
        maximumLargeBandMultiplier = PlayerPrefs.GetFloat(PreferencesPrefix + "SafetyCap", maximumLargeBandMultiplier);
        nativeWaveAmplitude = PlayerPrefs.GetFloat(PreferencesPrefix + "NativeWaveAmplitude", nativeWaveAmplitude);
        maximumWaterDecalLift = PlayerPrefs.GetFloat(PreferencesPrefix + "WaterDecalLift", maximumWaterDecalLift);
    }

    void OnDisable()
    {
        StopMicrophone();
        if (exhibitionWaterMaterial != null)
            Destroy(exhibitionWaterMaterial);
        if (nativeWaveSlots == null)
            return;

        for (int i = 0; i < nativeWaveSlots.Length; i++)
            if (nativeWaveSlots[i].material != null)
                Destroy(nativeWaveSlots[i].material);

        if (proceduralOceanMaterial != null)
            Destroy(proceduralOceanMaterial);
        if (proceduralOceanMesh != null)
            Destroy(proceduralOceanMesh);
        if (proceduralOceanObject != null)
            Destroy(proceduralOceanObject);
    }
}
