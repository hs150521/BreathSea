using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class InstallAudioWaveDecalAssets
{
    const string SourceFolder = "Library/PackageCache/com.unity.render-pipelines.high-definition@4d2fbd882056/Samples~/WaterSamples/Materials/RollingWave";
    const string DestinationFolder = "Assets/WaterSystemDemo/Resources";
    const string DestinationFolderWithSample = DestinationFolder + "/AudioWaveSample";
    const string Destination = DestinationFolderWithSample + "/WaveDeformer_SG.mat";
    const string PulseDestinationFolder = DestinationFolder + "/AudioPulseDecal";
    const string PulseDestination = PulseDestinationFolder + "/Water Circle Deformer.mat";
    const string PulseGraphDestination = PulseDestinationFolder + "/Water Circle Deformer.shadergraph";
    const string PulseRuntimeMaterial = PulseDestinationFolder + "/Audio Pulse Deformer.mat";
    const string PulseTextureDestination = PulseDestinationFolder + "/Circle.png";
    const string FoamGraph = "Library/PackageCache/com.unity.render-pipelines.high-definition@4d2fbd882056/Samples~/WaterSamples/Materials/Pool/Hot Tub Foam Deformer_SG.shadergraph";
    const string FoamGraphDestination = PulseDestinationFolder + "/Audio Foam Wave.shadergraph";
    const string FoamRuntimeMaterial = PulseDestinationFolder + "/Audio Foam Wave.mat";
    const string CurrentGraph = "Library/PackageCache/com.unity.render-pipelines.high-definition@4d2fbd882056/Samples~/WaterSamples/Materials/CurrentWithSplines/Sample Water Decal.shadergraph";
    const string CurrentGraphDestination = PulseDestinationFolder + "/Audio Current Flow.shadergraph";
    const string CurrentRuntimeMaterial = PulseDestinationFolder + "/Audio Current Flow.mat";
    const string CurrentTextureDestination = PulseDestinationFolder + "/Audio Current Field.png";
    const string PacketFolder = PulseDestinationFolder + "/Packets";
    const int PacketCount = 4;
    const string CircleGraph = "Library/PackageCache/com.unity.render-pipelines.high-definition@4d2fbd882056/Samples~/WaterSamples/Materials/Pool/Water Circle Deformer.shadergraph";
    const string CircleMaterial = "Library/PackageCache/com.unity.render-pipelines.high-definition@4d2fbd882056/Samples~/WaterSamples/Materials/Glacier/Water Circle Deformer.mat";
    const string CircleTexture = "Library/PackageCache/com.unity.render-pipelines.high-definition@4d2fbd882056/Samples~/WaterSamples/Textures/Pool/Circle.png";

    static InstallAudioWaveDecalAssets()
    {
        EditorApplication.delayCall += Install;
    }

    [MenuItem("BreathSea/Install Audio Wave Decal Asset")]
    static void Install()
    {
        if (!AssetDatabase.IsValidFolder(DestinationFolder))
            AssetDatabase.CreateFolder("Assets/WaterSystemDemo", "Resources");
        if (!AssetDatabase.IsValidFolder(PulseDestinationFolder))
            AssetDatabase.CreateFolder(DestinationFolder, "AudioPulseDecal");
        if (!AssetDatabase.IsValidFolder(PacketFolder))
            AssetDatabase.CreateFolder(PulseDestinationFolder, "Packets");

        if (!System.IO.Directory.Exists(SourceFolder) || !System.IO.File.Exists(CircleGraph))
        {
            Debug.LogError("BreathSea: HDRP water decal sample files are not available in PackageCache.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<Material>(Destination) == null)
            FileUtil.CopyFileOrDirectory(SourceFolder, DestinationFolderWithSample);

        // This decal only outputs deformation. Unlike RollingWave it never writes a
        // simulation mask, so sustained input cannot flatten a rectangular sea area.
        if (AssetDatabase.LoadAssetAtPath<Material>(PulseDestination) == null)
        {
            FileUtil.CopyFileOrDirectory(CircleGraph, PulseGraphDestination);
            FileUtil.CopyFileOrDirectory(CircleMaterial, PulseDestination);
            FileUtil.CopyFileOrDirectory(CircleTexture, PulseDestinationFolder + "/Circle.png");
        }

        if (!System.IO.File.Exists(FoamGraph))
        {
            Debug.LogError("BreathSea: HDRP foam water decal sample is not available in PackageCache.");
            return;
        }

        if (!System.IO.File.Exists(CurrentGraph))
        {
            Debug.LogError("BreathSea: HDRP current water decal sample is not available in PackageCache.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<Shader>(CurrentGraphDestination) == null)
        {
            FileUtil.CopyFileOrDirectory(CurrentGraph, CurrentGraphDestination);
            string graphText = System.IO.File.ReadAllText(CurrentGraphDestination);
            graphText = graphText.Replace("\"affectsDeformation\": true", "\"affectsDeformation\": false");
            graphText = graphText.Replace("\"affectsFoam\": true", "\"affectsFoam\": false");
            System.IO.File.WriteAllText(CurrentGraphDestination, graphText);
        }

        if (AssetDatabase.LoadAssetAtPath<Shader>(FoamGraphDestination) == null)
        {
            FileUtil.CopyFileOrDirectory(FoamGraph, FoamGraphDestination);
            string graphText = System.IO.File.ReadAllText(FoamGraphDestination);
            graphText = graphText.Replace("\"affectsDeformation\": true", "\"affectsDeformation\": false");
            System.IO.File.WriteAllText(FoamGraphDestination, graphText);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Shader pulseShader = AssetDatabase.LoadAssetAtPath<Shader>(PulseGraphDestination);
        Shader foamShader = AssetDatabase.LoadAssetAtPath<Shader>(FoamGraphDestination);
        Shader currentShader = AssetDatabase.LoadAssetAtPath<Shader>(CurrentGraphDestination);
        if (pulseShader != null && AssetDatabase.LoadAssetAtPath<Material>(PulseRuntimeMaterial) == null)
        {
            // The sample material is a variant of the package shader. A standalone
            // project material keeps the build independent of that package asset.
            Material pulseMaterial = new Material(pulseShader) { name = "Audio Pulse Deformer" };
            AssetDatabase.CreateAsset(pulseMaterial, PulseRuntimeMaterial);
        }
        if (foamShader != null && AssetDatabase.LoadAssetAtPath<Material>(FoamRuntimeMaterial) == null)
        {
            Material foamMaterial = new Material(foamShader) { name = "Audio Foam Wave" };
            foamMaterial.SetFloat("_Speed", 0.42f);
            foamMaterial.SetFloat("_Max_Amplitude", 1.2f);
            foamMaterial.SetVector("_Vector2", new Vector4(7f, 11f, 0f, 0f));
            AssetDatabase.CreateAsset(foamMaterial, FoamRuntimeMaterial);
        }
        CreateCurrentFieldTexture();
        if (currentShader != null && AssetDatabase.LoadAssetAtPath<Material>(CurrentRuntimeMaterial) == null)
        {
            Material currentMaterial = new Material(currentShader) { name = "Audio Current Flow" };
            currentMaterial.EnableKeyword("_TYPE_TEXTURE");
            currentMaterial.EnableKeyword("_AFFECTS_LARGE_CURRENT");
            currentMaterial.SetFloat("_AffectLargeCurrent", 1f);
            currentMaterial.SetFloat("_AffectDeformation", 0f);
            currentMaterial.SetFloat("_AffectFoam", 0f);
            currentMaterial.SetTexture("_CurrentMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CurrentTextureDestination));
            currentMaterial.SetVector("_Blend_Distance", new Vector4(0.28f, 0.28f, 0f, 0f));
            AssetDatabase.CreateAsset(currentMaterial, CurrentRuntimeMaterial);
        }
        Material runtimePulseMaterial = AssetDatabase.LoadAssetAtPath<Material>(PulseRuntimeMaterial);
        Texture2D circleTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(PulseTextureDestination);
        if (runtimePulseMaterial != null && circleTexture != null)
        {
            runtimePulseMaterial.SetTexture("_SampleTexture2D_b6ca83ee7a5744eda121f52ddeb1fa1d_Texture_1_Texture2D", circleTexture);
            EditorUtility.SetDirty(runtimePulseMaterial);
        }
        CreateWavePacketTextures();
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("BreathSea: Installed HDRP audio wave decal materials.");
    }

    static void CreateWavePacketTextures()
    {
        for (int packet = 0; packet < PacketCount; packet++)
        {
            string assetPath = PacketFolder + "/WavePacketV2_" + packet + ".exr";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null)
                continue;

            const int size = 192;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBAHalf, true, true);
            Vector2 centre = new Vector2(
                Mathf.Lerp(-0.18f, 0.18f, Hash01(packet, 1)),
                Mathf.Lerp(-0.14f, 0.14f, Hash01(packet, 2)));
            float angle = Mathf.Lerp(-24f, 24f, Hash01(packet, 3)) * Mathf.Deg2Rad;
            Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x / (float)(size - 1) - 0.5f) * 2f, (y / (float)(size - 1) - 0.5f) * 2f) - centre;
                float radial = p.magnitude;
                float edge = Mathf.SmoothStep(1.02f, 0.3f, radial);
                float packetValue = 0f;
                for (int band = 0; band < 6; band++)
                {
                    float bandAngle = angle + Mathf.Lerp(-0.85f, 0.85f, Hash01(packet, 10 + band));
                    Vector2 direction = new Vector2(Mathf.Cos(bandAngle), Mathf.Sin(bandAngle));
                    float wavelength = Mathf.Lerp(0.20f, 0.62f, Hash01(packet, 20 + band));
                    float phase = Hash01(packet, 30 + band) * Mathf.PI * 2f;
                    float amplitude = Mathf.Lerp(0.10f, 0.20f, Hash01(packet, 40 + band));
                    packetValue += Mathf.Sin(Vector2.Dot(p, direction) * Mathf.PI * 2f / wavelength + phase) * amplitude;
                }
                float alongAxis = Vector2.Dot(p, axis);
                float groupEnvelope = Mathf.Exp(-3.4f * radial * radial) * Mathf.Lerp(0.8f, 1.15f, Mathf.PerlinNoise((p.x + packet) * 2.7f, (p.y + packet * 3) * 2.7f));
                // HDRP's decal deformation map is unipolar. Keep separated crestlets
                // above the background surface instead of encoding negative pit values.
                packetValue = Mathf.Clamp01(Mathf.Max(0f, packetValue) * edge * groupEnvelope * (0.82f + 0.18f * Mathf.Cos(alongAxis * Mathf.PI * 1.7f)) * 1.35f);
                texture.SetPixel(x, y, new Color(packetValue, packetValue, packetValue, 1f));
            }

            System.IO.File.WriteAllBytes(assetPath, texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }
    }

    static float Hash01(int packet, int seed)
    {
        return Mathf.Repeat(Mathf.Sin((packet + 1) * 12.9898f + seed * 78.233f) * 43758.5453f, 1f);
    }

    static void CreateCurrentFieldTexture()
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(CurrentTextureDestination) != null)
            return;

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (float)(size - 1);
            float v = y / (float)(size - 1);
            float angle = Mathf.Sin(u * Mathf.PI * 2f) * 0.7f + Mathf.Sin(v * Mathf.PI * 3f + 1.4f) * 0.45f + Mathf.Sin((u + v) * Mathf.PI * 5f) * 0.2f;
            Vector2 flow = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Lerp(0.28f, 0.62f, Mathf.PerlinNoise(u * 3.2f, v * 3.2f));
            texture.SetPixel(x, y, new Color(flow.x * 0.5f + 0.5f, flow.y * 0.5f + 0.5f, 1f, 1f));
        }
        System.IO.File.WriteAllBytes(CurrentTextureDestination, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(CurrentTextureDestination, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(CurrentTextureDestination) as TextureImporter;
        if (importer != null)
        {
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }
}
