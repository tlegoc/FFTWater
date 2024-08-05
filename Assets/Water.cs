using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Water : MonoBehaviour
{
    public ComputeShader waterCompute;
    public ComputeShader FFTCompute;
    public Shader waterShader;

    [Header("Plane body parameter")] public float size = 100f;

    // Resolution
    private int _N = 1024;

    [Header("Phillips parameters")] [Tooltip("Wind direction and speed.")]
    public Vector2 _w;

    [Tooltip("Phillips parameter")] public float _A = 1.8e-05f;

    [Tooltip("Accentuate the directionality of the waves.")]
    public float _WavePower = 6.0f;

    [Header("Additionnal parameters")] [Tooltip("Multiply the displacement in the vertex shader.")]
    public float AmplitudeOverride = 1f;

    public Texture2D noiseTexture;

    [Tooltip("Controls noise generation. Should allow you to sync waves between clients.")]
    public int seed = 0;

    private RenderTexture _noiseTextureInternal;

    private RenderTexture _HT0;
    private RenderTexture _HT;
    private RenderTexture _FFTFirstPass;
    private RenderTexture _HTSlopeX, _HTSlopeZ;
    private RenderTexture _HTDx, _HTDz;
    private RenderTexture _Displacement, _Normals;

    private Material _waterMaterial;

    private bool _isInitialized = false;

    // Helpers
    public void NewSeed()
    {
        seed = Random.Range(0, Int32.MaxValue);
    }

    public void GenerateNoiseTexture()
    {
        _noiseTextureInternal = CreateRenderTex(_N, _N, 1, RenderTextureFormat.ARGBFloat, false, true);

        // Create tex
        waterCompute.SetInt("_Seed", seed);
        waterCompute.SetTexture(0, "_noiseTextureInternal", _noiseTextureInternal);
        waterCompute.Dispatch(0, _N / 8, _N / 8, 1);

        // Copy data to noiseTexture
        noiseTexture = new Texture2D(_N, _N, TextureFormat.RGBAFloat, false);
        noiseTexture.name = "Noise Texture";
        RenderTexture.active = _noiseTextureInternal;
        noiseTexture.ReadPixels(new Rect(0, 0, _N, _N), 0, 0);
        noiseTexture.Apply();
        RenderTexture.active = null;

#if UNITY_EDITOR
        DestroyImmediate(_noiseTextureInternal);
#else
        Destroy(_noiseTextureInternal);
#endif
    }

    // Stolen from https://github.com/GarrettGunnell/Water/blob/main/Assets/Scripts/FFTWater.cs#L384
    RenderTexture CreateRenderTex(int width, int height, int depth, RenderTextureFormat format, bool useMips,
        bool noFilter = false)
    {
        RenderTexture rt = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
        if (depth > 1)
            rt.dimension = TextureDimension.Tex2DArray;
        else
            rt.dimension = TextureDimension.Tex2D;
        rt.filterMode = noFilter ? FilterMode.Point : FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Repeat;
        rt.enableRandomWrite = true;
        rt.volumeDepth = depth;
        rt.useMipMap = useMips;
        rt.autoGenerateMips = false;
        rt.anisoLevel = 16;
        rt.Create();

        return rt;
    }

    public RenderTexture GetHT0()
    {
        return _HT0;
    }

    void OnEnable()
    {
        Init();
    }

    void InitMesh()
    {
        Mesh m = new Mesh();
        m.name = "Water Grid";
        m.indexFormat = IndexFormat.UInt32;

        Vector3[] vertices = new Vector3[(_N + 1) * (_N + 1)];
        Vector2[] uv = new Vector2[vertices.Length];

        float step = size / _N;
        float halfSize = size / 2f;

        for (int i = 0, x = 0; x <= _N; x++)
        {
            for (int y = 0; y <= _N; y++, i++)
            {
                vertices[i] = new Vector3(x * step - halfSize, 0, y * step - halfSize);
                uv[i] = new Vector2((float)x / _N, (float)y / _N);
            }
        }

        m.vertices = vertices;
        m.uv = uv;

        int[] triangles = new int[_N * _N * 6];

        for (int ti = 0, vi = 0, x = 0; x < _N; ++vi, ++x)
        {
            for (int z = 0; z < _N; ti += 6, ++vi, ++z)
            {
                triangles[ti] = vi + 1;
                triangles[ti + 1] = vi + _N + 1;
                triangles[ti + 2] = vi;
                triangles[ti + 3] = vi + _N + 2;
                triangles[ti + 4] = vi + _N + 1;
                triangles[ti + 5] = vi + 1;
            }
        }

        m.triangles = triangles;

        GetComponent<MeshFilter>().mesh = m;

        _waterMaterial = new Material(waterShader);
        GetComponent<MeshRenderer>().material = _waterMaterial;
    }

    void InitOceanData()
    {
        _HT0 = CreateRenderTex(_N, _N, 1, RenderTextureFormat.ARGBFloat, false, true);
        _HT = CreateRenderTex(_N, _N, 1, RenderTextureFormat.RGFloat, false, true);
        _FFTFirstPass = CreateRenderTex(_N, _N, 1, RenderTextureFormat.RGFloat, false, true);
        _HTSlopeX = CreateRenderTex(_N, _N, 1, RenderTextureFormat.RGFloat, false, true);
        _HTSlopeZ = CreateRenderTex(_N, _N, 1, RenderTextureFormat.RGFloat, false, true);
        _HTDx = CreateRenderTex(_N, _N, 1, RenderTextureFormat.RGFloat, false, true);
        _HTDz = CreateRenderTex(_N, _N, 1, RenderTextureFormat.RGFloat, false, true);
        _Displacement = CreateRenderTex(_N, _N, 1, RenderTextureFormat.ARGBFloat, false, true);
        _Normals = CreateRenderTex(_N, _N, 1, RenderTextureFormat.ARGBFloat, false, true);

        // Initial spectrum computation
        waterCompute.SetTexture(1, "_noiseTexture", noiseTexture);
        waterCompute.SetFloat("_L", size);
        waterCompute.SetInt("_N", _N);
        waterCompute.SetVector("_W", _w);
        waterCompute.SetFloat("_A", _A);
        waterCompute.SetFloat("_WavePower", _WavePower);
        waterCompute.SetTexture(1, "_HT0", _HT0);

        waterCompute.Dispatch(1, _N / 8, _N / 8, 1);
    }

    public void Init()
    {
        if (!noiseTexture)
        {
            Debug.Log("You must specify a noise texture or recreate one.");
            return;
        }

        InitMesh();
        InitOceanData();

        _isInitialized = true;

        SetShaderParameter();
    }

    void SetShaderParameter()
    {
        _waterMaterial.SetFloat("_AmplitudeMult", AmplitudeOverride);
        _waterMaterial.SetTexture("_Displacement", _Displacement);
        _waterMaterial.SetTexture("_Normals", _Normals);
    }

    // Update is called once per frame
    void Update()
    {
        if (!_isInitialized) return;

        // Compute HT
        waterCompute.SetFloat("time", Time.time);
        waterCompute.SetTexture(2, "_HT0", _HT0);
        waterCompute.SetTexture(2, "_HT", _HT);
        waterCompute.SetTexture(2, "_HTSlopeX", _HTSlopeX);
        waterCompute.SetTexture(2, "_HTSlopeZ", _HTSlopeX);
        waterCompute.SetTexture(2, "_HTDx", _HTDx);
        waterCompute.SetTexture(2, "_HTDz", _HTDz);
        waterCompute.Dispatch(2, _N / 8, _N / 8, 1);

        // FFT for each map
        FFTCompute.SetTexture(0, "TextureSource", _HT);
        FFTCompute.SetTexture(0, "TextureTarget", _FFTFirstPass);
        FFTCompute.Dispatch(0, 1, _N, 1);
        FFTCompute.SetTexture(1, "TextureSource", _FFTFirstPass);
        FFTCompute.SetTexture(1, "TextureTarget", _HT);
        FFTCompute.Dispatch(1, 1, _N, 1);

        FFTCompute.SetTexture(0, "TextureSource", _HTSlopeX);
        FFTCompute.SetTexture(0, "TextureTarget", _FFTFirstPass);
        FFTCompute.Dispatch(0, 1, _N, 1);
        FFTCompute.SetTexture(1, "TextureSource", _FFTFirstPass);
        FFTCompute.SetTexture(1, "TextureTarget", _HTSlopeX);
        FFTCompute.Dispatch(1, 1, _N, 1);

        FFTCompute.SetTexture(0, "TextureSource", _HTSlopeZ);
        FFTCompute.SetTexture(0, "TextureTarget", _FFTFirstPass);
        FFTCompute.Dispatch(0, 1, _N, 1);
        FFTCompute.SetTexture(1, "TextureSource", _FFTFirstPass);
        FFTCompute.SetTexture(1, "TextureTarget", _HTSlopeZ);
        FFTCompute.Dispatch(1, 1, _N, 1);

        FFTCompute.SetTexture(0, "TextureSource", _HTDx);
        FFTCompute.SetTexture(0, "TextureTarget", _FFTFirstPass);
        FFTCompute.Dispatch(0, 1, _N, 1);
        FFTCompute.SetTexture(1, "TextureSource", _FFTFirstPass);
        FFTCompute.SetTexture(1, "TextureTarget", _HTDx);
        FFTCompute.Dispatch(1, 1, _N, 1);

        FFTCompute.SetTexture(0, "TextureSource", _HTDz);
        FFTCompute.SetTexture(0, "TextureTarget", _FFTFirstPass);
        FFTCompute.Dispatch(0, 1, _N, 1);
        FFTCompute.SetTexture(1, "TextureSource", _FFTFirstPass);
        FFTCompute.SetTexture(1, "TextureTarget", _HTDz);
        FFTCompute.Dispatch(1, 1, _N, 1);

        // Permute step (could be done directly in FFT)
        waterCompute.SetTexture(3, "_permute", _HT);
        waterCompute.Dispatch(3, _N / 8, _N / 8, 1);

        waterCompute.SetTexture(3, "_permute", _HTSlopeX);
        waterCompute.Dispatch(3, _N / 8, _N / 8, 1);

        waterCompute.SetTexture(3, "_permute", _HTSlopeZ);
        waterCompute.Dispatch(3, _N / 8, _N / 8, 1);

        waterCompute.SetTexture(3, "_permute", _HTDx);
        waterCompute.Dispatch(3, _N / 8, _N / 8, 1);

        waterCompute.SetTexture(3, "_permute", _HTDz);
        waterCompute.Dispatch(3, _N / 8, _N / 8, 1);

        // Final computation
        waterCompute.SetTexture(4, "_HT", _HT);
        waterCompute.SetTexture(4, "_HTSlopeX", _HTSlopeX);
        waterCompute.SetTexture(4, "_HTSlopeZ", _HTSlopeZ);
        waterCompute.SetTexture(4, "_HTDx", _HTDx);
        waterCompute.SetTexture(4, "_HTDz", _HTDz);
        waterCompute.SetTexture(4, "_Displacement", _Displacement);
        waterCompute.SetTexture(4, "_Normals", _Normals);
        waterCompute.Dispatch(4, _N / 8, _N / 8, 1);
    }

    public void CleanupTextures()
    {
#if UNITY_EDITOR
        DestroyImmediate(_noiseTextureInternal);
        DestroyImmediate(_HT0);
        DestroyImmediate(_HT);
        DestroyImmediate(_HTSlopeX);
        DestroyImmediate(_HTSlopeZ);
        DestroyImmediate(_HTDx);
        DestroyImmediate(_HTDz);
        DestroyImmediate(_Displacement);
        DestroyImmediate(_Normals);
#else
        Destroy(_noiseTextureInternal);
        Destroy(_HT0);
        Destroy(_HT);
        Destroy(_HTSlopeX);
        Destroy(_HTSlopeZ);
        Destroy(_HTDx);
        Destroy(_HTDz);
        Destroy(_Displacement);
        Destroy(_Normals);
#endif
    }

    public void Cleanup()
    {
        if (_waterMaterial)
        {
#if UNITY_EDITOR
            DestroyImmediate(_waterMaterial);
#else
            Destroy(_waterMaterial);
#endif
            _waterMaterial = null;
        }

        CleanupTextures();
    }

    private void OnDisable()
    {
        Cleanup();
    }
}

// Custom editor
#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(Water))]
public class WaterEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);
        GUILayout.Label("How to use:\n" +
                        "1. Create a new noise texture or use an existing one.\nChanging the seed will change the noise.\n" +
                        "2. Initialize the water.\n" +
                        "3. Play with the parameters.\n" +
                        "4. Done!\n");
        Water water = (Water)target;
        if (GUILayout.Button("Initialize"))
        {
            water.Cleanup();
            water.Init();
        }

        if (GUILayout.Button("New seed"))
        {
            water.NewSeed();
        }

        if (GUILayout.Button("Recreate noise texture"))
        {
            water.GenerateNoiseTexture();
        }

        if (GUILayout.Button("Full reinit"))
        {
            water.Cleanup();
            water.NewSeed();
            water.GenerateNoiseTexture();
            water.Init();
        }
    }
}
#endif