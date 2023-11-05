using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Water : MonoBehaviour
{
    public ComputeShader waterFFT;
    public Shader waterShader;
    public bool _debug = false;

    [Header("Plane body parameter")] public float size = 100f;

    public int gridSizePowerOfTwo = 8;

    public int seed = 0;

    private int _N;

    private float L
    {
        get { return size; }
    }

    [Header("Phillips parameters")] [Tooltip("Wind direction")]
    public Vector2 _w;

    [Tooltip("Wind speed")] public float _V; // Wind speed
    [Tooltip("Phillips parameter")] public float _A;

    [Header("Additionnal parameters")] public float AmplitudeOverride = 1f;

    private RenderTexture _h0Spectrum, _Spectrum, _Heightmap, _htildeDisplacement, _htildeSlope, _PostHorizontalDFT;
    private Material _waterMaterial;

    // Start is called before the first frame update
    void OnEnable()
    {
        Init();
    }

    public void Init()
    {
        _N = 1 << gridSizePowerOfTwo;
        seed = Random.Range(0, 10000000);
        CreateTextures();
        CreateGrid();
        // CS_Computeh0Spectrum
        SetComputeParameters(0);
        waterFFT.Dispatch(0, _N, _N, 1);
        if (_debug)
            SaveTexture(_h0Spectrum, "h0Spectrum");
    }

    public void CreateGrid()
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

    // Stolen from https://github.com/GarrettGunnell/Water/blob/main/Assets/Scripts/FFTWater.cs#L384
    RenderTexture CreateRenderTex(int width, int height, RenderTextureFormat format, bool useMips)
    {
        RenderTexture rt = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Repeat;
        rt.enableRandomWrite = true;
        rt.useMipMap = useMips;
        rt.autoGenerateMips = false;
        rt.enableRandomWrite = true;
        rt.anisoLevel = 16;
        rt.Create();

        return rt;
    }

    void CreateTextures()
    {
        _h0Spectrum = CreateRenderTex(_N, _N, RenderTextureFormat.ARGBHalf, true);

        _Spectrum = CreateRenderTex(_N, _N, RenderTextureFormat.ARGBHalf, true);

        _Heightmap = CreateRenderTex(_N, _N, RenderTextureFormat.ARGBHalf, true);

        _htildeDisplacement = CreateRenderTex(_N, _N, RenderTextureFormat.ARGBHalf, true);

        _htildeSlope = CreateRenderTex(_N, _N, RenderTextureFormat.ARGBHalf, true);

        _PostHorizontalDFT = CreateRenderTex(_N, _N, RenderTextureFormat.ARGBHalf, true);
    }

    void SetComputeParameters(int kernel = 0)
    {
        waterFFT.SetInt("_N", _N);
        waterFFT.SetFloat("_L", L);
        waterFFT.SetVector("_w", _w);
        waterFFT.SetFloat("_V", _V);
        waterFFT.SetFloat("_A", _A);
        waterFFT.SetFloat("_t", Time.time);
        waterFFT.SetFloat("_dt", Time.deltaTime);
        waterFFT.SetFloat("_Seed", seed);
        waterFFT.SetVector("_offsets",
            new Vector3((Random.value - 0.5f) * 1000.0f, (Random.value - 0.5f) * 1000.0f,
                (Random.value - 0.5f) * 1000.0f));
        waterFFT.SetTexture(kernel, "_h0spectrum", _h0Spectrum);
        waterFFT.SetTexture(kernel, "_Spectrum", _Spectrum);
        waterFFT.SetTexture(kernel, "_Heightmap", _Heightmap);
        waterFFT.SetTexture(kernel, "_htildeDisplacement", _htildeDisplacement);
        waterFFT.SetTexture(kernel, "_htildeSlope", _htildeSlope);
        waterFFT.SetTexture(kernel, "_PostHorizontalDFT", _PostHorizontalDFT);
    }

    void SetShaderParameter()
    {
        _waterMaterial.SetTexture("_Heightmap", _Heightmap);
        _waterMaterial.SetFloat("_AmplitudeMult", AmplitudeOverride);
    }


    // Used for debugging
    void SaveTexture(RenderTexture tex, string name = "picture")
    {
        Texture2D t = new Texture2D(tex.width, tex.height, TextureFormat.RGBAFloat, false);

        RenderTexture.active = tex;
        t.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        t.Apply();

        t.EncodeToPNG();
        System.IO.File.WriteAllBytes(Application.dataPath + "/../" + name + ".png", t.EncodeToPNG());
        Debug.Log("Image saved to " + Application.dataPath + "/../" + name + ".png");
    }

    // Used for debug purposes
    private int i = 0;

    // Update is called once per frame
    void Update()
    {
        // CS_Computehtilde
        SetComputeParameters(1);
        waterFFT.Dispatch(1, _N, _N, 1);

        // Actual FFT
        // CS_HorizontalDFT
        SetComputeParameters(2);
        waterFFT.Dispatch(2, _N, _N, 1);
        // CS_VerticalDFT
        SetComputeParameters(3);
        waterFFT.Dispatch(3, _N, _N, 1);

        SetShaderParameter();

#if UNITY_EDITOR
        // If is playing mode
        if (!Application.isPlaying && false)
        {
            SaveTexture(_Heightmap, "heightmap");

            SaveTexture(_Spectrum, "spectrum");
            SaveTexture(_PostHorizontalDFT, "posthoriz");
        }
#endif
    }

    public void CleanupTextures()
    {
#if UNITY_EDITOR
        DestroyImmediate(_h0Spectrum);
        DestroyImmediate(_Spectrum);
        DestroyImmediate(_Heightmap);
        DestroyImmediate(_htildeDisplacement);
        DestroyImmediate(_htildeSlope);
        DestroyImmediate(_PostHorizontalDFT);
#else
        Destroy(_h0Spectrum);
        Destroy(_Spectrum);
        Destroy(_Heightmap);
        Destroy(_htildeDisplacement);
        Destroy(_htildeSlope);
        Destroy(_PostHorizontalDFT);
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

        Water water = (Water)target;
        if (GUILayout.Button("Generate Grid"))
        {
            water.Cleanup();
            water.Init();
        }
    }
}
#endif