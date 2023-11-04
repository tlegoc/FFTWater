using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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

    private RenderTexture _h0Spectrum, _Spectrum, _Heightmap, _htildeDisplacement, _htildeSlope, _PostHorizontalDFT;
    private Material _waterMaterial;

    public void GenerateGrid()
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
                uv[i] = new Vector2((float) x  / _N, (float) y / _N);
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

    // Start is called before the first frame update
    void OnEnable()
    {
        _N = 1 << gridSizePowerOfTwo;
        CreateResources();
        GenerateGrid();
        // CS_Computeh0Spectrum
        SetComputeParameters(0);
        waterFFT.Dispatch(0, _N, _N, 1);
        if (_debug)
            SaveTexture(_h0Spectrum, "h0Spectrum");
    }

    void CreateResources()
    {
        _h0Spectrum = new RenderTexture(_N, _N, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        _h0Spectrum.filterMode = FilterMode.Bilinear;
        _h0Spectrum.wrapMode = TextureWrapMode.Repeat;
        _h0Spectrum.enableRandomWrite = true;
        _h0Spectrum.autoGenerateMips = false;
        _h0Spectrum.anisoLevel = 16;
        _h0Spectrum.Create();

        _Spectrum = new RenderTexture(_N, _N, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        _Spectrum.filterMode = FilterMode.Bilinear;
        _Spectrum.wrapMode = TextureWrapMode.Repeat;
        _Spectrum.enableRandomWrite = true;
        _Spectrum.autoGenerateMips = false;
        _Spectrum.anisoLevel = 16;
        _Spectrum.Create();
        
        _Heightmap = new RenderTexture(_N, _N, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        _Heightmap.filterMode = FilterMode.Bilinear;
        _Heightmap.wrapMode = TextureWrapMode.Repeat;
        _Heightmap.enableRandomWrite = true;
        _Heightmap.autoGenerateMips = false;
        _Heightmap.anisoLevel = 16;
        _Heightmap.Create();
        
        _htildeDisplacement = new RenderTexture(_N, _N, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        _htildeDisplacement.filterMode = FilterMode.Bilinear;
        _htildeDisplacement.wrapMode = TextureWrapMode.Repeat;
        _htildeDisplacement.enableRandomWrite = true;
        _htildeDisplacement.autoGenerateMips = false;
        _htildeDisplacement.anisoLevel = 16;
        _htildeDisplacement.Create();
        
        _htildeSlope = new RenderTexture(_N, _N, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        _htildeSlope.filterMode = FilterMode.Bilinear;
        _htildeSlope.wrapMode = TextureWrapMode.Repeat;
        _htildeSlope.enableRandomWrite = true;
        _htildeSlope.autoGenerateMips = false;
        _htildeSlope.anisoLevel = 16;
        _htildeSlope.Create();
        
        _PostHorizontalDFT = new RenderTexture(_N, _N, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        _PostHorizontalDFT.filterMode = FilterMode.Bilinear;
        _PostHorizontalDFT.wrapMode = TextureWrapMode.Repeat;
        _PostHorizontalDFT.enableRandomWrite = true;
        _PostHorizontalDFT.autoGenerateMips = false;
        _PostHorizontalDFT.anisoLevel = 16;
        _PostHorizontalDFT.Create();
        
    }

    void CreateDebugResources()
    {
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
            new Vector3((Random.value - 0.5f) * 1000.0f, (Random.value - 0.5f) * 1000.0f, (Random.value - 0.5f) * 1000.0f));
        waterFFT.SetTexture(kernel, "_h0spectrum", _h0Spectrum);
        waterFFT.SetTexture(kernel, "_Spectrum", _Spectrum);
        waterFFT.SetTexture(kernel, "_Heightmap", _Heightmap);
        waterFFT.SetTexture(kernel, "_htildeDisplacement", _htildeDisplacement);
        waterFFT.SetTexture(kernel, "_htildeSlope", _htildeSlope);
        waterFFT.SetTexture(kernel, "_PostHorizontalDFT", _PostHorizontalDFT);
    }

    void SetShaderParameter()
    {       
        _waterMaterial.SetTexture("_Heightmap", _Spectrum);
    }

    private int i = 0;
    // Update is called once per frame
    void Update()
    {
        // CS_Computehtilde
        SetComputeParameters(1);
        waterFFT.Dispatch(1, _N, _N, 1);
        // CS_HorizontalDFT
        SetComputeParameters(2);
        waterFFT.Dispatch(2, _N, _N, 1);
        // CS_VerticalDFT
        SetComputeParameters(3);
        waterFFT.Dispatch(3, _N, _N, 1);

        SetShaderParameter();
        
        
        
        if (Input.GetButtonDown("Fire1"))
        {
            SaveTexture(_Heightmap, "heightmap");
            i = 0;
        }

        if (Input.GetButton("Fire1") && i < 1)
        {
            SaveTexture(_Spectrum, "spectrum" + i++);
            SaveTexture(_PostHorizontalDFT, "posthoriz" + i);
        }
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
}