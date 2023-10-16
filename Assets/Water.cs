using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Water : MonoBehaviour
{
    public ComputeShader waterFFT;
    public Shader waterShader;
    
    [Header("Plane body parameter")] [Range(0f, 2000f)]
    public float size = 1000f;

    [Range(0, 2048)] public int gridSize = 128;

    [Header("Body parameters")]
    int _N;
    float _L;

    [Header("Phillips parameters")]
    [Tooltip("Wind direction")]
    public Vector2 _w;
    [Tooltip("Wind speed")]
    public float _V; // Wind speed
    [Tooltip("Phillips parameter")]
    public float _A;

    private RenderTexture _result;
    private Material _waterMaterial;
    
    public void GenerateGrid()
    {
        Mesh m = new Mesh();
        m.name = "Water Grid";

        Vector3[] vertices = new Vector3[(gridSize + 1) * (gridSize + 1)];
        Vector2[] uv = new Vector2[vertices.Length];

        float step = size / gridSize;
        float halfSize = size / 2f;

        for (int i = 0, x = 0; x <= gridSize; x++)
        {
            for (int y = 0; y <= gridSize; y++, i++)
            {
                vertices[i] = new Vector3(x * step - halfSize, 0, y * step - halfSize);
                uv[i] = new Vector2(x / (float)gridSize, y / (float)gridSize);
            }
        }

        m.vertices = vertices;
        m.uv = uv;

        int[] triangles = new int[gridSize * gridSize * 6];

        for (int ti = 0, vi = 0, x = 0; x < gridSize; ++vi, ++x)
        {
            for (int z = 0; z < gridSize; ti += 6, ++vi, ++z)
            {
                triangles[ti] = vi + 1;
                triangles[ti + 1] = vi + gridSize + 1;
                triangles[ti + 2] = vi;
                triangles[ti + 3] = vi + gridSize + 2;
                triangles[ti + 4] = vi + gridSize + 1;
                triangles[ti + 5] = vi + 1;
            }
        }

        m.triangles = triangles;

        m.RecalculateNormals();
        GetComponent<MeshFilter>().mesh = m;

        _waterMaterial = new Material(waterShader);
        GetComponent<MeshRenderer>().material = _waterMaterial;
    }

    void GenerateSpectrumTextures()
    {
        
    }

    // Start is called before the first frame update
    void OnEnable()
    {
        CreateResource();
        GenerateGrid();
        SetShaderParameter();
        SetComputeParameters();
    }

    void CreateResource()
    {
        _result = new RenderTexture(gridSize, gridSize, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        _result.filterMode = FilterMode.Bilinear;   
        _result.wrapMode = TextureWrapMode.Repeat;
        _result.enableRandomWrite = true;
        _result.autoGenerateMips = false;
        _result.enableRandomWrite = true;
        _result.anisoLevel = 16;
        _result.Create();
    }

    void SetComputeParameters()
    {
        waterFFT.SetInt("_N", _N);
        waterFFT.SetFloat("_L", _L);
        waterFFT.SetVector("_w", _w);
        waterFFT.SetFloat("_V", _V);
        waterFFT.SetFloat("_A", _A);
        waterFFT.SetFloat("_t", Time.deltaTime);
        waterFFT.SetTexture(0, "_result", _result);
    }

    void SetShaderParameter()
    {
        _waterMaterial.SetTexture("_HeightField", _result);
    }

    // Update is called once per frame
    void Update()
    {
        SetComputeParameters();
        waterFFT.Dispatch(0 , gridSize, gridSize, 1);
    }
}