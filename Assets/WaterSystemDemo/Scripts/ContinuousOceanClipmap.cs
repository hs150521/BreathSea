using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
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
    const int WaveCount = 14;

    struct WaveComponent
    {
        public Vector2 direction;
        public Vector2 groupDirection;
        public float wavelength;
        public float amplitude;
        public float phase;
        public float groupPhase;
    }

    struct WaveComponentJob
    {
        public float2 direction;
        public float2 groupDirection;
        public float wavelength;
        public float amplitude;
        public float phase;
        public float groupPhase;
    }

    [BurstCompile]
    struct SurfaceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> grid;
        [ReadOnly] public NativeArray<WaveComponentJob> waves;
        public NativeArray<float3> vertices;
        public NativeArray<float3> normals;
        public float3 anchor;
        public float3 right;
        public float3 forward;
        public float nearDistance;
        public float farDistance;
        public float nearHalfWidth;
        public float farHalfWidth;
        public float energy;
        public float baseLift;
        public float time;

        public void Execute(int index)
        {
            float2 coordinate = grid[index];
            float z = nearDistance + (farDistance - nearDistance) * (math.exp(coordinate.y * 4.6f) - 1f) / (math.exp(4.6f) - 1f);
            float halfWidth = math.lerp(nearHalfWidth, farHalfWidth, math.saturate((z - nearDistance) / (farDistance - nearDistance)));
            float x = math.lerp(-halfWidth, halfWidth, coordinate.x);
            float3 world = anchor + right * x + forward * z;
            float2 warped = DomainWarp(world.x, world.z);
            world.x += warped.x;
            world.z += warped.y;

            float3 position = world + new float3(0f, baseLift, 0f);
            float3 tangent = new float3(1f, 0f, 0f);
            float3 binormal = new float3(0f, 0f, 1f);
            float2 worldXZ = world.xz;

            for (int i = 0; i < waves.Length; i++)
            {
                WaveComponentJob wave = waves[i];
                float waveNumber = 6.2831853f / wave.wavelength;
                float groupA = math.sin(math.dot(wave.groupDirection, worldXZ) * (0.018f + i * 0.0009f) + wave.groupPhase + time * 0.11f);
                float groupB = math.sin(math.dot(wave.direction, worldXZ) * (0.007f + i * 0.00035f) + wave.phase * 1.7f - time * 0.07f);
                float groupEnvelope = math.lerp(0.42f, 1f, 0.5f + 0.32f * groupA + 0.18f * groupB);
                float amplitude = wave.amplitude * energy * groupEnvelope;
                float phase = waveNumber * math.dot(wave.direction, worldXZ) + math.sqrt(9.81f * waveNumber) * time + wave.phase;
                float steepness = math.min(0.46f, 0.19f / math.max(0.02f, waveNumber * amplitude * waves.Length));
                float sine = math.sin(phase);
                float cosine = math.cos(phase);
                float horizontal = steepness * amplitude;

                position.x += wave.direction.x * horizontal * cosine;
                position.z += wave.direction.y * horizontal * cosine;
                position.y += amplitude * sine;
                tangent += new float3(-wave.direction.x * wave.direction.x * steepness * amplitude * waveNumber * sine, wave.direction.x * amplitude * waveNumber * cosine, -wave.direction.x * wave.direction.y * steepness * amplitude * waveNumber * sine);
                binormal += new float3(-wave.direction.x * wave.direction.y * steepness * amplitude * waveNumber * sine, wave.direction.y * amplitude * waveNumber * cosine, -wave.direction.y * wave.direction.y * steepness * amplitude * waveNumber * sine);
            }

            float3 normal = math.normalize(math.cross(binormal, tangent));
            if (normal.y < 0f) normal = -normal;
            float3 delta = position - anchor;
            vertices[index] = new float3(math.dot(delta, right), delta.y, math.dot(delta, forward));
            normals[index] = new float3(math.dot(normal, right), normal.y, math.dot(normal, forward));
        }

        static float2 DomainWarp(float x, float z)
        {
            float largeA = x * 0.0091f + z * 0.0047f + 0.7f;
            float largeB = x * -0.0037f + z * 0.0103f + 2.1f;
            float middleA = x * 0.028f + z * -0.019f + 1.6f;
            float middleB = x * 0.017f + z * 0.034f + 4.4f;
            float smallA = x * -0.074f + z * 0.061f + 0.9f;
            float smallB = x * 0.053f + z * 0.082f + 3.7f;
            return new float2(
                31f * (0.58f * math.sin(largeA) + 0.42f * math.cos(largeB)) + 13f * math.sin(middleA) + 4f * math.cos(smallA),
                31f * (0.46f * math.cos(largeA) - 0.54f * math.sin(largeB)) + 13f * math.cos(middleB) + 4f * math.sin(smallB));
        }
    }

    static readonly WaveComponent[] Waves = CreateWaveSet();
    static readonly Vector2[] DetailDirections =
    {
        new Vector2(3f, 7f), new Vector2(-8f, 5f), new Vector2(11f, -4f),
        new Vector2(17f, 9f), new Vector2(-13f, 16f), new Vector2(23f, -11f)
    };
    static readonly float[] DetailWeights = { 0.62f, 0.43f, 0.29f, 0.18f, 0.11f, 0.07f };

    static WaveComponent[] CreateWaveSet()
    {
        WaveComponent[] components = new WaveComponent[WaveCount];
        for (int i = 0; i < components.Length; i++)
        {
            float normalizedIndex = i / (float)(components.Length - 1);
            float wavelength = 3.1f * Mathf.Pow(1.2f, i);
            float spread = Mathf.Lerp(0.38f, 2.4f, normalizedIndex);
            float centre = 0.56f + (i % 6 == 0 ? 1.18f : 0f);
            float angle = centre + (Hash01(i * 19 + 7) - 0.5f) * spread;
            float groupAngle = angle + (Hash01(i * 29 + 11) - 0.5f) * 1.0f;
            components[i] = new WaveComponent
            {
                direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                groupDirection = new Vector2(Mathf.Cos(groupAngle), Mathf.Sin(groupAngle)),
                wavelength = wavelength,
                amplitude = 0.00185f * Mathf.Pow(wavelength, 1.05f),
                phase = Hash01(i * 43 + 5) * Mathf.PI * 2f,
                groupPhase = Hash01(i * 61 + 17) * Mathf.PI * 2f
            };
        }
        return components;
    }

    static float Hash01(int value)
    {
        uint hash = (uint)value;
        hash ^= hash >> 16;
        hash *= 0x7feb352du;
        hash ^= hash >> 15;
        hash *= 0x846ca68bu;
        hash ^= hash >> 16;
        return (hash & 0x00ffffffu) / 16777215f;
    }
    [Header("References")]
    public Transform referenceCamera;
    public WaterSurface originalWater;
    [Tooltip("Render this mesh through the assigned HDRP Water Surface instead of a separate material.")]
    public bool useHdrpWaterRenderer = false;

    [Header("Continuous Surface")]
    [Range(80, 192)] public int columns = 112;
    [Range(64, 176)] public int rows = 88;
    public float nearDistance = -80f;
    public float farDistance = 5000f;
    public float nearHalfWidth = 900f;
    public float farHalfWidth = 9000f;
    [Range(0f, 48f)] public float largeDomainWarp = 31f;
    [Range(0f, 24f)] public float middleDomainWarp = 13f;
    [Range(0f, 10f)] public float smallDomainWarp = 4f;
    [Range(0.35f, 3f)] public float calmEnergy = 0.65f;
    [Range(0.5f, 3f)] public float activeEnergy = 1.08f;
    [Range(0f, 1f)] public float audioEnergy;
    public float baseLift = 0.5f;

    Mesh mesh;
    Material material;
    MeshRenderer meshRenderer;
    Vector3[] vertices;
    Vector3[] normals;
    Vector4[] tangents;
    Vector2[] gridCoordinates;
    NativeArray<float2> nativeGrid;
    NativeArray<WaveComponentJob> nativeWaves;
    NativeArray<float3> nativeVertices;
    NativeArray<float3> nativeNormals;
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
        DisposeNativeArrays();
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
        nativeGrid = new NativeArray<float2>(vertices.Length, Allocator.Persistent);
        nativeVertices = new NativeArray<float3>(vertices.Length, Allocator.Persistent);
        nativeNormals = new NativeArray<float3>(vertices.Length, Allocator.Persistent);
        nativeWaves = new NativeArray<WaveComponentJob>(Waves.Length, Allocator.Persistent);
        for (int i = 0; i < Waves.Length; i++)
        {
            WaveComponent source = Waves[i];
            nativeWaves[i] = new WaveComponentJob
            {
                direction = new float2(source.direction.x, source.direction.y),
                groupDirection = new float2(source.groupDirection.x, source.groupDirection.y),
                wavelength = source.wavelength,
                amplitude = source.amplitude,
                phase = source.phase,
                groupPhase = source.groupPhase
            };
        }
        int[] triangles = new int[columnCount * rowCount * 6];

        int vertex = 0;
        for (int z = 0; z <= rowCount; z++)
        for (int x = 0; x <= columnCount; x++)
        {
            gridCoordinates[vertex] = new Vector2(x / (float)columnCount, z / (float)rowCount);
            nativeGrid[vertex] = new float2(gridCoordinates[vertex].x, gridCoordinates[vertex].y);
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
        material.SetFloat("_Smoothness", 0.91f);
        // The previous 512px normal was tiled dozens of times over the clipmap.
        // That made a procedural normal field read as visible, evenly spaced bands.
        // Geometric normals already contain the full wave hierarchy and stay coupled
        // to its displacement, so leave the material free of repeated texture detail.
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

        float energy = Mathf.Lerp(calmEnergy, activeEnergy, Mathf.SmoothStep(0f, 1f, audioEnergy));
        SurfaceJob job = new SurfaceJob
        {
            grid = nativeGrid,
            waves = nativeWaves,
            vertices = nativeVertices,
            normals = nativeNormals,
            anchor = new float3(anchor.x, anchor.y, anchor.z),
            right = new float3(yaw * Vector3.right),
            forward = new float3(yaw * Vector3.forward),
            nearDistance = nearDistance,
            farDistance = farDistance,
            nearHalfWidth = nearHalfWidth,
            farHalfWidth = farHalfWidth,
            energy = energy,
            baseLift = baseLift,
            time = Time.time
        };
        job.Schedule(nativeVertices.Length, 64).Complete();
        mesh.SetVertices(nativeVertices);
        mesh.SetNormals(nativeNormals);
        mesh.bounds = new Bounds(new Vector3(0f, 0f, (nearDistance + farDistance) * 0.5f), new Vector3(farHalfWidth * 2.2f, 8f, farDistance - nearDistance + 50f));
    }

    void DisposeNativeArrays()
    {
        if (nativeGrid.IsCreated) nativeGrid.Dispose();
        if (nativeWaves.IsCreated) nativeWaves.Dispose();
        if (nativeVertices.IsCreated) nativeVertices.Dispose();
        if (nativeNormals.IsCreated) nativeNormals.Dispose();
    }

    Vector3 EvaluateGerstner(Vector3 world, float energy, out Vector3 normal)
    {
        Vector3 position = world + Vector3.up * baseLift;
        Vector3 tangent = Vector3.right;
        Vector3 binormal = Vector3.forward;
        float time = Time.time;

        for (int i = 0; i < Waves.Length; i++)
        {
            WaveComponent wave = Waves[i];
            float waveNumber = Mathf.PI * 2f / wave.wavelength;
            Vector2 worldXZ = new Vector2(world.x, world.z);
            float groupA = Mathf.Sin(Vector2.Dot(wave.groupDirection, worldXZ) * (0.018f + i * 0.0009f) + wave.groupPhase + time * 0.11f);
            float groupB = Mathf.Sin(Vector2.Dot(wave.direction, worldXZ) * (0.007f + i * 0.00035f) + wave.phase * 1.7f - time * 0.07f);
            float groupEnvelope = Mathf.Lerp(0.42f, 1f, 0.5f + 0.32f * groupA + 0.18f * groupB);
            float amplitude = wave.amplitude * energy * groupEnvelope;
            float phase = waveNumber * Vector2.Dot(wave.direction, worldXZ) + Mathf.Sqrt(9.81f * waveNumber) * time + wave.phase;
            float steepness = Mathf.Min(0.46f, 0.19f / Mathf.Max(0.02f, waveNumber * amplitude * Waves.Length));
            float sine = Mathf.Sin(phase);
            float cosine = Mathf.Cos(phase);
            float horizontal = steepness * amplitude;

            position.x += wave.direction.x * horizontal * cosine;
            position.z += wave.direction.y * horizontal * cosine;
            position.y += amplitude * sine;

            tangent += new Vector3(-wave.direction.x * wave.direction.x * steepness * amplitude * waveNumber * sine, wave.direction.x * amplitude * waveNumber * cosine, -wave.direction.x * wave.direction.y * steepness * amplitude * waveNumber * sine);
            binormal += new Vector3(-wave.direction.x * wave.direction.y * steepness * amplitude * waveNumber * sine, wave.direction.y * amplitude * waveNumber * cosine, -wave.direction.y * wave.direction.y * steepness * amplitude * waveNumber * sine);
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
            float phase = Mathf.PI * 2f * (DetailDirections[i].x * u + DetailDirections[i].y * v + Hash01(i * 73 + 2));
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
