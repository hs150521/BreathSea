#pragma warning disable 0618

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public class BreathToWater : MonoBehaviour
{
    [Header("Target Water")]
    public WaterSurface water;

    [Header("Camera / Direction")]
    public Transform referenceCamera;

    [Header("Traveling Pulse Deformer")]
    public WaterDeformer pulseDeformerTemplate;
    public int pulsePoolSize = 3;

    public float pulseNearDistance = 8f;
    public float pulseFarDistance = 150f;
    public float pulseTravelTime = 12f;
    public float pulseAmplitude = 1f;
    public float pulseWidth = 120f;
    public float pulseLength = 35f;
    public float pulseSideOffset = 0f;

    [Header("Microphone")]
    public string microphoneName = "";
    public int sampleRate = 44100;
    public int sampleWindow = 1024;
    public int recordingLengthSeconds = 10;

    [Header("Mic Calibration")]
    public float noiseFloor = 0.004f;
    public float breathCeiling = 0.022f;
    public float breathGain = 5f;

    [Header("Breath Envelope")]
    public float attackSpeed = 2.5f;
    public float releaseSpeed = 0.7f;
    public float responseCurve = 1.6f;

    [Header("Breath Pulse Trigger")]
    [Range(0f, 1f)]
    public float pulseThreshold = 0.12f;

    public float pulseCooldown = 1f;
    public float triggerPulseStrength = 1f;

    [Header("Slow Swell")]
    public float slowSwellStrength = 0.22f;
    public float slowSwellMinSpeed = 0.06f;
    public float slowSwellMaxSpeed = 0.18f;

    [Header("Delayed Global Ocean")]
    [Tooltip("全局大海浪变强的速度。越低，远处响应越慢。")]
    public float globalAttackSpeed = 0.35f;

    [Tooltip("全局大海浪恢复平静的速度。越低，恢复越慢。")]
    public float globalReleaseSpeed = 0.25f;

    [Range(0f, 1f)]
    public float delayedGlobalWave;

    [Header("Global Water Influence")]
    [Range(0f, 1f)]
    public float globalWaterInfluence = 0.4f;

    [Range(0f, 1f)]
    public float rippleInfluence = 0.1f;

    [Range(0f, 1f)]
    public float foamInfluence = 0.15f;

    [Header("Calm Ocean")]
    public float calmDistantWindSpeed = 18f;
    public float calmChaos = 0.25f;
    public float calmFirstBand = 0.45f;
    public float calmSecondBand = 0.45f;
    public float calmRipplesWindSpeed = 1.5f;
    public float calmRipplesChaos = 0.2f;
    public float calmTimeMultiplier = 0.75f;
    public float calmFoam = 0.25f;

    [Header("Breathing Ocean")]
    public float activeDistantWindSpeed = 55f;
    public float activeChaos = 0.45f;
    public float activeFirstBand = 1.2f;
    public float activeSecondBand = 1.2f;
    public float activeRipplesWindSpeed = 3.5f;
    public float activeRipplesChaos = 0.45f;
    public float activeTimeMultiplier = 1.15f;
    public float activeFoam = 0.45f;

    [Header("Debug Test")]
    public KeyCode testPulseKey = KeyCode.T;

    [Header("Debug")]
    [Range(0f, 1f)]
    public float breathValue;

    [Range(0f, 1f)]
    public float waveValue;

    [Range(0f, 1f)]
    public float pulseValue;

    [Range(0f, 1f)]
    public float slowSwellValue;

    public float rawMicLevel;
    public float maxRawMicLevel;

    private AudioClip micClip;
    private float[] samples;

    private float smoothedBreath;
    private float smoothedGlobalWave;
    private float slowSwellPhase;

    private bool wasAbovePulseThreshold;
    private float lastPulseTime = -999f;

    private PulseSlot[] pulseSlots;

    private class PulseSlot
    {
        public WaterDeformer deformer;
        public float age;
        public float strength;
        public Vector3 start;
        public Vector3 end;
        public bool active;
    }

    void Start()
    {
        if (water == null)
        {
            Debug.LogError("BreathToWater: 请把 Water > Ocean 拖到 Water 槽里。");
            enabled = false;
            return;
        }

        if (referenceCamera == null && Camera.main != null)
        {
            referenceCamera = Camera.main.transform;
        }

        if (referenceCamera == null)
        {
            Debug.LogError("BreathToWater: 请把你的固定 Camera 拖到 Reference Camera 槽里。");
            enabled = false;
            return;
        }

        samples = new float[sampleWindow];

        SetupMicrophone();
        SetupPulseDeformers();
    }

    void SetupMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("BreathToWater: 没有检测到麦克风。");
            enabled = false;
            return;
        }

        foreach (string device in Microphone.devices)
        {
            Debug.Log("Found microphone: " + device);
        }

        if (string.IsNullOrEmpty(microphoneName))
        {
            microphoneName = Microphone.devices[0];
        }

        micClip = Microphone.Start(
            microphoneName,
            true,
            recordingLengthSeconds,
            sampleRate
        );

        Debug.Log("Microphone started: " + microphoneName);
    }

    void SetupPulseDeformers()
    {
        if (pulseDeformerTemplate == null)
        {
            Debug.LogWarning("BreathToWater: 没有设置 Pulse Deformer Template，将只使用全局波浪变化。");
            return;
        }

        pulseSlots = new PulseSlot[pulsePoolSize];

        pulseDeformerTemplate.amplitude = 0f;
        pulseDeformerTemplate.gameObject.SetActive(false);

        for (int i = 0; i < pulsePoolSize; i++)
        {
            GameObject obj = Instantiate(
                pulseDeformerTemplate.gameObject,
                pulseDeformerTemplate.transform.parent
            );

            obj.name = "Breath Traveling Pulse " + i;
            obj.SetActive(false);

            WaterDeformer deformer = obj.GetComponent<WaterDeformer>();

            pulseSlots[i] = new PulseSlot
            {
                deformer = deformer,
                active = false
            };
        }
    }

    void Update()
    {
        rawMicLevel = GetMicRMS();
        maxRawMicLevel = Mathf.Max(maxRawMicLevel, rawMicLevel);

        float normalized = Mathf.InverseLerp(noiseFloor, breathCeiling, rawMicLevel);
        normalized = Mathf.Clamp01(normalized);

        normalized *= breathGain;
        normalized = Mathf.Clamp01(normalized);

        float envelopeSpeed = normalized > smoothedBreath ? attackSpeed : releaseSpeed;

        smoothedBreath = Mathf.Lerp(
            smoothedBreath,
            normalized,
            1f - Mathf.Exp(-envelopeSpeed * Time.deltaTime)
        );

        breathValue = smoothedBreath;

        float baseWave = Mathf.Pow(breathValue, responseCurve);
        baseWave = Mathf.Clamp01(baseWave);

        if (Input.GetKeyDown(testPulseKey))
        {
            StartTravelingPulse(1f);
        }

        TryTriggerTravelingPulse(baseWave);
        UpdateTravelingPulses();

        UpdateDelayedGlobalWave(baseWave);
        UpdateSlowSwell(delayedGlobalWave);

        waveValue = Mathf.Clamp01(baseWave);
        ApplyGlobalWater(delayedGlobalWave);
    }

    void UpdateDelayedGlobalWave(float targetWave)
    {
        float speed = targetWave > smoothedGlobalWave
            ? globalAttackSpeed
            : globalReleaseSpeed;

        smoothedGlobalWave = Mathf.Lerp(
            smoothedGlobalWave,
            targetWave,
            1f - Mathf.Exp(-speed * Time.deltaTime)
        );

        delayedGlobalWave = smoothedGlobalWave;
    }

    void TryTriggerTravelingPulse(float baseWave)
    {
        if (baseWave < pulseThreshold)
            return;

        if (Time.time - lastPulseTime < pulseCooldown)
            return;

        StartTravelingPulse(Mathf.Clamp01(baseWave * triggerPulseStrength));
        lastPulseTime = Time.time;
    }

    void StartTravelingPulse(float strength)
    {
        if (pulseSlots == null || pulseSlots.Length == 0)
            return;

        PulseSlot slot = null;

        for (int i = 0; i < pulseSlots.Length; i++)
        {
            if (!pulseSlots[i].active)
            {
                slot = pulseSlots[i];
                break;
            }
        }

        if (slot == null)
        {
            slot = pulseSlots[0];
        }

        Vector3 forward = referenceCamera.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = referenceCamera.transform.forward;
            forward.y = 0f;
        }

        forward.Normalize();

        Vector3 right = referenceCamera.right;
        right.y = 0f;
        right.Normalize();

        Vector3 origin = referenceCamera.position + right * pulseSideOffset;

        float waterY = pulseDeformerTemplate != null
            ? pulseDeformerTemplate.transform.position.y
            : water.transform.position.y;

        Vector3 start = origin + forward * pulseNearDistance;
        Vector3 end = origin + forward * pulseFarDistance;

        start.y = waterY;
        end.y = waterY;

        slot.age = 0f;
        slot.strength = Mathf.Max(0.2f, strength);
        slot.start = start;
        slot.end = end;
        slot.active = true;

        slot.deformer.gameObject.SetActive(true);
        slot.deformer.amplitude = 0f;

        slot.deformer.regionSize = new Vector2(pulseWidth, pulseLength);
        slot.deformer.boxBlend = new Vector2(pulseWidth * 0.45f, pulseLength * 0.45f);
        slot.deformer.cubicBlend = true;

        slot.deformer.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        slot.deformer.transform.position = start;
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

            // 新逻辑：
            // 前 10% 淡入，中间保持，最后 15% 才淡出。
            // 这样一次呼吸触发后，波浪会自己走到远处，而不是依赖持续输入。
            float fadeIn = Mathf.SmoothStep(0f, 0.1f, t);
            float fadeOut = 1f - Mathf.SmoothStep(0.85f, 1f, t);
            float envelope = fadeIn * fadeOut;

            // 远处略微加强，避免到远处看不见。
            float farBoost = Mathf.Lerp(1f, 1.35f, moveT);

            float currentAmplitude =
                pulseAmplitude *
                slot.strength *
                envelope *
                farBoost;

            slot.deformer.transform.position = Vector3.Lerp(slot.start, slot.end, moveT);
            slot.deformer.amplitude = currentAmplitude;

            // 越往远处，波带稍微变宽，看起来更像扩散出去的大浪。
            float widthScale = Mathf.Lerp(0.8f, 1.4f, moveT);

            slot.deformer.regionSize = new Vector2(
                pulseWidth * widthScale,
                pulseLength
            );

            pulseValue = Mathf.Max(
                pulseValue,
                Mathf.Clamp01(envelope * slot.strength)
            );
        }
    }

    void UpdateSlowSwell(float baseWave)
    {
        float swellSpeed = Mathf.Lerp(
            slowSwellMinSpeed,
            slowSwellMaxSpeed,
            baseWave
        );

        slowSwellPhase += swellSpeed * Mathf.PI * 2f * Time.deltaTime;

        float sine = Mathf.Sin(slowSwellPhase) * 0.5f + 0.5f;

        slowSwellValue = sine * slowSwellStrength * baseWave;
    }

    float GetMicRMS()
    {
        if (micClip == null || !Microphone.IsRecording(microphoneName))
            return 0f;

        int micPosition = Microphone.GetPosition(microphoneName);

        if (micPosition < 0)
            return 0f;

        int clipSamples = micClip.samples;
        int startPosition = micPosition - sampleWindow;

        if (startPosition < 0)
        {
            startPosition += clipSamples;
        }

        micClip.GetData(samples, startPosition);

        float sum = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return Mathf.Sqrt(sum / samples.Length);
    }

    void ApplyGlobalWater(float delayedWave)
    {
        // 大浪响应：不要再乘得太小，否则 active 参数再大也看不出来
        float largeInput = Mathf.Clamp01(delayedWave + slowSwellValue * 0.5f);

        // 稍微平滑，但不要压得太狠
        float largeT = Mathf.SmoothStep(0f, 1f, largeInput);

        // 小波纹单独压低，避免看起来只是碎波
        float rippleT = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.Clamp01(delayedWave * rippleInfluence)
        );

        // 泡沫也弱一点
        float foamT = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.Clamp01(delayedWave * foamInfluence)
        );

        water.largeWindSpeed = Mathf.Lerp(
            calmDistantWindSpeed,
            activeDistantWindSpeed,
            largeT
        );

        water.largeChaos = Mathf.Lerp(
            calmChaos,
            activeChaos,
            largeT
        );

        water.largeBand0Multiplier = Mathf.Lerp(
            calmFirstBand,
            activeFirstBand,
            largeT
        );

        water.largeBand1Multiplier = Mathf.Lerp(
            calmSecondBand,
            activeSecondBand,
            largeT
        );

        water.ripplesWindSpeed = Mathf.Lerp(
            calmRipplesWindSpeed,
            activeRipplesWindSpeed,
            rippleT
        );

        water.ripplesChaos = Mathf.Lerp(
            calmRipplesChaos,
            activeRipplesChaos,
            rippleT
        );

        water.timeMultiplier = Mathf.Lerp(
            calmTimeMultiplier,
            activeTimeMultiplier,
            largeT
        );

        water.simulationFoamAmount = Mathf.Lerp(
            calmFoam,
            activeFoam,
            foamT
        );
    }


    [ContextMenu("Apply Fixed Defaults From Screenshot")]
    public void ApplyFixedDefaultsFromScreenshot()
    {
        pulsePoolSize = 3;
        pulseNearDistance = 8f;
        pulseFarDistance = 150f;
        pulseTravelTime = 12f;
        pulseAmplitude = 1f;
        pulseWidth = 120f;
        pulseLength = 35f;
        pulseSideOffset = 0f;

        sampleRate = 44100;
        sampleWindow = 1024;
        recordingLengthSeconds = 10;

        noiseFloor = 0.004f;
        breathCeiling = 0.022f;
        breathGain = 5f;

        attackSpeed = 2.5f;
        releaseSpeed = 0.7f;
        responseCurve = 1.6f;

        pulseThreshold = 0.12f;
        pulseCooldown = 1f;
        triggerPulseStrength = 1f;

        slowSwellStrength = 0.22f;
        slowSwellMinSpeed = 0.06f;
        slowSwellMaxSpeed = 0.18f;

        globalAttackSpeed = 0.35f;
        globalReleaseSpeed = 0.25f;

        globalWaterInfluence = 0.4f;
        rippleInfluence = 0.1f;
        foamInfluence = 0.15f;

        calmDistantWindSpeed = 18f;
        calmChaos = 0.25f;
        calmFirstBand = 0.45f;
        calmSecondBand = 0.45f;
        calmRipplesWindSpeed = 1.5f;
        calmRipplesChaos = 0.2f;
        calmTimeMultiplier = 0.75f;
        calmFoam = 0.25f;

        activeDistantWindSpeed = 55f;
        activeChaos = 0.45f;
        activeFirstBand = 1.2f;
        activeSecondBand = 1.2f;
        activeRipplesWindSpeed = 3.5f;
        activeRipplesChaos = 0.45f;
        activeTimeMultiplier = 1.15f;
        activeFoam = 0.45f;
    }

    void OnDisable()
    {
        if (!string.IsNullOrEmpty(microphoneName) && Microphone.IsRecording(microphoneName))
        {
            Microphone.End(microphoneName);
        }
    }
}