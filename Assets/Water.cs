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

    private int _N = 1024;

    [Header("Phillips parameters")] [Tooltip("Wind direction and speed")]
    public Vector2 _w;

    [Tooltip("Phillips parameter")] public float _A = 1.8e-05f;
    public float _WavePower = 6.0f;

    [Header("Additionnal parameters")] public float AmplitudeOverride = 1f;

    public Texture2D noiseTexture;
    [Tooltip("Controls noise generation")] public int seed = 0;

    private RenderTexture _noiseTextureInternal;

    private RenderTexture _HT0;

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

        m.RecalculateNormals();
        GetComponent<MeshFilter>().mesh = m;

        _waterMaterial = new Material(waterShader);
        GetComponent<MeshRenderer>().material = _waterMaterial;
    }

    void InitOceanData()
    {
        _HT0 = CreateRenderTex(_N, _N, 1, RenderTextureFormat.ARGBFloat, false);

        waterCompute.SetTexture(1, "_noiseTexture", noiseTexture);
        waterCompute.SetFloat("_L", size);
        waterCompute.SetInt("_N", _N);
        waterCompute.SetVector("_W", _w);
        waterCompute.SetFloat("_A", _A);
        waterCompute.SetFloat("_WavePower", _WavePower);
        waterCompute.SetTexture(1, "_HT0", _HT0);
        
        waterCompute.Dispatch(1, _N/8, _N/8, 1);
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
    }

    void SetShaderParameter()
    {
        _waterMaterial.SetFloat("_AmplitudeMult", AmplitudeOverride);
    }


    // Used for debugging

    #region DEBUG_FUNCTIONS

    public void SaveTexture(RenderTexture tex, string path)
    {
        Texture2D t = new Texture2D(tex.width, tex.height, TextureFormat.RGBAFloat, false);

        RenderTexture.active = tex;
        t.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        t.Apply();

        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        Debug.Log("Image saved to " + path);
    }

    public void SaveNoiseTexture(string path)
    {
        System.IO.File.WriteAllBytes(path, noiseTexture.EncodeToPNG());
        Debug.Log("Image saved to " + path);
    }

    #endregion

    // Update is called once per frame
    void Update()
    {
        if (!_isInitialized) return;

        // Compute htilde

        // FFT
    }

    public void CleanupTextures()
    {
#if UNITY_EDITOR
        DestroyImmediate(_noiseTextureInternal);
        DestroyImmediate(_HT0);
#else
        Destroy(_noiseTextureInternal);
        Destroy(_HT0);
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

        GUILayout.Label("Debug");

        if (GUILayout.Button("Save noise texture"))
        {
            string path = EditorUtility.SaveFilePanel("Save noise texture", "", "noise", "png");
            if (path != "") water.SaveNoiseTexture(path);
        }

        if (GUILayout.Button("Save HT0"))
        {
            string path = EditorUtility.SaveFilePanel("Save HT0 texture", "", "ht0", "png");
            if (path != "") water.SaveTexture(water.GetHT0(), path);
        }
    }
}
#endif