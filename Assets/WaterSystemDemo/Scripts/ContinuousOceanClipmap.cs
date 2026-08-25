using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// A camera-following mesh that feeds the stock HDRP water renderer. Its warped
/// horizontal coordinates break up periodic FFT sampling without replacing HDRP
/// water lighting, reflections, displacement, or shoreline interactions.
/// </summary>
[DefaultExecutionOrder(20)]
public sealed class ContinuousOceanClipmap : MonoBehaviour
{
    static readonly Vector2[] WaveDirections =
    {
        new Vector2(0.88f, 0.47f), new Vector2(0.61f, 0.79f), new Vector2(-0.18f, 0.98f),
        new Vector2(-0.71f, 0.70f), new Vector2(0.98f, -0.20f), new Vector2(0.39f, -0.92f),
        new Vector2(-0.84f, -0.54f), new Vector2(-0.42f, 0.91f), new Vector2(0.22f, 0.98f),
        new Vector2(0.77f, -0.64f)
    };
    static readonly float[] Wavelengths = { 185f, 118f, 76f, 49f, 31f, 20f, 13f, 8.2f, 5.3f, 3.4f };
    static readonly float[] Amplitudes = { 0.82f, 0.54f, 0.34f, 0.21f, 0.13f, 0.08f, 0.050f, 0.030f, 0.018f, 0.011f };
    static readonly float[] Phases = { 0.2f, 1.4f, 2.9f, 4.7f, 5.6f, 0.8f, 3.6f, 2.1f, 5.0f, 1.1f };
    static readonly Vector2[] DetailDirections =
    {
        new Vector2(3f, 7f), new Vector2(-8f, 5f), new Vector2(11f, -4f),
        new Vector2(17f, 9f), new Vector2(-13f, 16f), new Vector2(23f, -11f)
    };
    static readonly float[] DetailWeights = { 0.62f, 0.43f, 0.29f, 0.18f, 0.11f, 0.07f };
    [Header("References")]
    public Transform referenceCamera;
    public WaterSurface originalWater;
    [Tooltip("Render this mesh through the assigned HDRP Water Surface instead of a separate material.")]
    public bool useHdrpWaterRenderer = false;

    [Header("Continuous Surface")]
    [Range(80, 192)] public int columns = 144;
    [Range(64, 176)] public int rows = 120;
    public float nearDistance = -28f;
    public float farDistance = 1450f;
    public float nearHalfWidth = 150f;
    public float farHalfWidth = 1320f;
    [Range(0f, 48f)] public float largeDomainWarp = 31f;
    [Range(0f, 24f)] public float middleDomainWarp = 13f;
    [Range(0f, 10f)] public float smallDomainWarp = 4f;
    [Range(0.35f, 3f)] public float calmEnergy = 0.65f;
    [Range(0.5f, 3f)] public float activeEnergy = 1.08f;
    [Range(0f, 1f)] public float audioEnergy;
    public float baseLift = 0.5f;

    Mesh mesh;
    Material material;
    GpuOceanSpectrum spectrum;
    MeshRenderer meshRenderer;
    Vector3[] vertices;
    Vector3[] normals;
    Vector4[] tangents;
    Vector2[] gridCoordinates;
    List<MeshRenderer> originalMeshRenderers;
    WaterGeometryType originalGeometryType;
    bool originalWaterWasEnabled;

    public void Configure(Transform cameraTransform, WaterSurface water)
    {
        referenceCamera = cameraTransform;
        originalWater = water;
        CreateSurface();
        ConfigureWaterRenderer();
    }

    void OnEnable()
    {
        if (referenceCamera == null && Camera.main != null)
            referenceCamera = Camera.main.transform;

        CreateSurface();
        ConfigureWaterRenderer();
    }

    void OnDisable()
    {
        RestoreWaterRenderer();

        if (Application.isPlaying)
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
        else
        {
            if (mesh != null) DestroyImmediate(mesh);
            if (material != null) DestroyImmediate(material);
        }
        mesh = null;
        material = null;
    }

    void CreateSurface()
    {
        if (mesh != null || referenceCamera == null)
            return;

        Shader shader = Shader.Find("HDRP/Lit");
        if (shader == null)
        {
            Debug.LogError("ContinuousOceanClipmap: HDRP/Lit is unavailable for the mesh holder.");
            enabled = false;
            return;
        }

        int columnCount = Mathf.Clamp(columns, 80, 192);
        int rowCount = Mathf.Clamp(rows, 64, 176);
        vertices = new Vector3[(columnCount + 1) * (rowCount + 1)];
        normals = new Vector3[vertices.Length];
        tangents = new Vector4[vertices.Length];
        gridCoordinates = new Vector2[vertices.Length];
        int[] triangles = new int[columnCount * rowCount * 6];

        int vertex = 0;
        for (int z = 0; z <= rowCount; z++)
        for (int x = 0; x <= columnCount; x++)
        {
            gridCoordinates[vertex] = new Vector2(x / (float)columnCount, z / (float)rowCount);
            normals[vertex] = Vector3.up;
            tangents[vertex] = new Vector4(1f, 0f, 0f, 1f);
            vertex++;
        }

        int triangle = 0;
        for (int z = 0; z < rowCount; z++)
        for (int x = 0; x < columnCount; x++)
        {
            int a = z * (columnCount + 1) + x;
            int b = a + 1;
            int c = a + columnCount + 1;
            int d = c + 1;
            triangles[triangle++] = a;
            triangles[triangle++] = c;
            triangles[triangle++] = b;
            triangles[triangle++] = b;
            triangles[triangle++] = c;
            triangles[triangle++] = d;
        }

        mesh = new Mesh { name = "Continuous Exhibition Ocean" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.uv = gridCoordinates;
        mesh.triangles = triangles;
        mesh.MarkDynamic();

        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
        material = new Material(shader) { name = "Continuous Multi-Scale Ocean Material" };
        material.SetColor("_BaseColor", new Color(0.018f, 0.11f, 0.14f, 1f));
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.72f);
        spectrum = GetComponent<GpuOceanSpectrum>();
        if (spectrum == null) spectrum = gameObject.AddComponent<GpuOceanSpectrum>();
        spectrum.Configure(referenceCamera);
        material.SetTexture("_NormalMap", spectrum.normalFoamTexture);
        material.SetTextureScale("_NormalMap", new Vector2(42f, 37f));
        material.SetFloat("_NormalScale", 0.8f);
        material.EnableKeyword("_NORMALMAP");
        meshRenderer.sharedMaterial = material;
        meshRenderer.enabled = !useHdrpWaterRenderer;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        meshRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        UpdateSurface();
    }

    void LateUpdate()
    {
        UpdateSurface();
        if (spectrum != null)
            spectrum.audioEnergy = audioEnergy;
    }

    void ConfigureWaterRenderer()
    {
        if (originalWater == null || meshRenderer == null)
            return;

        if (!useHdrpWaterRenderer)
        {
            originalWaterWasEnabled = originalWater.enabled;
            originalWater.enabled = false;
            meshRenderer.enabled = true;
            return;
        }

        originalWaterWasEnabled = originalWater.enabled;
        originalGeometryType = originalWater.geometryType;
        originalMeshRenderers = new List<MeshRenderer>(originalWater.meshRenderers);
        originalWater.geometryType = WaterGeometryType.Custom;
        originalWater.meshRenderers.Clear();
        originalWater.meshRenderers.Add(meshRenderer);
        originalWater.customMaterial = null;
        originalWater.enabled = true;
        meshRenderer.enabled = false;
    }

    void RestoreWaterRenderer()
    {
        if (originalWater == null)
            return;

        if (!useHdrpWaterRenderer)
        {
            originalWater.enabled = originalWaterWasEnabled;
            return;
        }

        originalWater.geometryType = originalGeometryType;
        originalWater.meshRenderers.Clear();
        if (originalMeshRenderers != null)
            originalWater.meshRenderers.AddRange(originalMeshRenderers);
        originalWater.enabled = originalWaterWasEnabled;
    }

    void UpdateSurface()
    {
        if (referenceCamera == null || mesh == null)
            return;

        Vector3 forward = referenceCamera.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();
        Quaternion yaw = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 anchor = referenceCamera.position;
        anchor.x = Mathf.Round(anchor.x / 4f) * 4f;
        anchor.z = Mathf.Round(anchor.z / 4f) * 4f;
        anchor.y = originalWater != null ? originalWater.transform.position.y : transform.position.y;
        transform.SetPositionAndRotation(anchor, yaw);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 grid = gridCoordinates[i];
            // Logarithmic distance distributes the geometry where a viewer can resolve it.
            float z = nearDistance + (farDistance - nearDistance) * (Mathf.Exp(grid.y * 4.6f) - 1f) / (Mathf.Exp(4.6f) - 1f);
            float halfWidth = Mathf.Lerp(nearHalfWidth, farHalfWidth, Mathf.Clamp01((z - nearDistance) / (farDistance - nearDistance)));
            float x = Mathf.Lerp(-halfWidth, halfWidth, grid.x);
            Vector3 world = anchor + yaw * new Vector3(x, 0f, z);
            Vector2 warp = EvaluateDomainWarp(world.x, world.z);
            world.x += warp.x;
            world.z += warp.y;
            float energy = Mathf.Lerp(calmEnergy, activeEnergy, Mathf.SmoothStep(0f, 1f, audioEnergy));
            Vector3 displacedWorld = EvaluateGerstner(world, energy, out Vector3 normal);
            vertices[i] = Quaternion.Inverse(yaw) * (displacedWorld - anchor);
            normals[i] = Quaternion.Inverse(yaw) * normal;
            tangents[i] = new Vector4(1f, 0f, 0f, 1f);
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.bounds = new Bounds(new Vector3(0f, 0f, (nearDistance + farDistance) * 0.5f), new Vector3(farHalfWidth * 2.2f, 8f, farDistance - nearDistance + 50f));
    }

    Vector3 EvaluateGerstner(Vector3 world, float energy, out Vector3 normal)
    {
        Vector3 position = world + Vector3.up * baseLift;
        Vector3 tangent = Vector3.right;
        Vector3 binormal = Vector3.forward;
        float time = Time.time;

        for (int i = 0; i < WaveDirections.Length; i++)
        {
            Vector2 direction = WaveDirections[i];
            float waveNumber = Mathf.PI * 2f / Wavelengths[i];
            float groupPhase = Vector2.Dot(new Vector2(0.011f + i * 0.0017f, -0.009f + i * 0.0013f), new Vector2(world.x, world.z)) + Phases[(i + 3) % Phases.Length];
            float groupEnvelope = 0.62f + 0.38f * (0.5f + 0.5f * Mathf.Sin(groupPhase));
            float amplitude = Amplitudes[i] * energy * groupEnvelope;
            float phase = waveNumber * Vector2.Dot(direction, new Vector2(world.x, world.z)) + Mathf.Sqrt(9.81f * waveNumber) * time + Phases[i];
            float steepness = Mathf.Min(0.48f, 0.22f / Mathf.Max(0.02f, waveNumber * amplitude * WaveDirections.Length));
            float sine = Mathf.Sin(phase);
            float cosine = Mathf.Cos(phase);
            float horizontal = steepness * amplitude;

            position.x += direction.x * horizontal * cosine;
            position.z += direction.y * horizontal * cosine;
            position.y += amplitude * sine;

            tangent += new Vector3(-direction.x * direction.x * steepness * amplitude * waveNumber * sine, direction.x * amplitude * waveNumber * cosine, -direction.x * direction.y * steepness * amplitude * waveNumber * sine);
            binormal += new Vector3(-direction.x * direction.y * steepness * amplitude * waveNumber * sine, direction.y * amplitude * waveNumber * cosine, -direction.y * direction.y * steepness * amplitude * waveNumber * sine);
        }

        normal = Vector3.Cross(binormal, tangent).normalized;
        if (normal.y < 0f) normal = -normal;
        return position;
    }

    Texture2D BuildDetailNormalMap(int resolution)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
        {
            name = "Continuous Ocean Detail Normal",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8
        };

        Color[] pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float u = x / (float)resolution;
            float v = y / (float)resolution;
            float dx = DetailDerivative(u, v, true);
            float dz = DetailDerivative(u, v, false);
            Vector3 normal = new Vector3(-dx * 0.24f, 1f, -dz * 0.24f).normalized;
            pixels[y * resolution + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    float DetailDerivative(float u, float v, bool xAxis)
    {
        float derivative = 0f;
        for (int i = 0; i < DetailDirections.Length; i++)
        {
            float phase = Mathf.PI * 2f * (DetailDirections[i].x * u + DetailDirections[i].y * v) + Phases[i];
            derivative += DetailWeights[i] * Mathf.PI * 2f * (xAxis ? DetailDirections[i].x : DetailDirections[i].y) * Mathf.Cos(phase);
        }
        return derivative;
    }

    Vector2 EvaluateDomainWarp(float x, float z)
    {
        float largeA = (x * 0.0091f + z * 0.0047f + 0.7f);
        float largeB = (x * -0.0037f + z * 0.0103f + 2.1f);
        float middleA = (x * 0.028f + z * -0.019f + 1.6f);
        float middleB = (x * 0.017f + z * 0.034f + 4.4f);
        float smallA = (x * -0.074f + z * 0.061f + 0.9f);
        float smallB = (x * 0.053f + z * 0.082f + 3.7f);
        return new Vector2(
            largeDomainWarp * (0.58f * Mathf.Sin(largeA) + 0.42f * Mathf.Cos(largeB)) + middleDomainWarp * Mathf.Sin(middleA) + smallDomainWarp * Mathf.Cos(smallA),
            largeDomainWarp * (0.46f * Mathf.Cos(largeA) - 0.54f * Mathf.Sin(largeB)) + middleDomainWarp * Mathf.Cos(middleB) + smallDomainWarp * Mathf.Sin(smallB));
    }
}
