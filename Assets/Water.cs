using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class Water : MonoBehaviour
{
    [Header("Plane body parameter")] [Range(0f, 2000f)]
    public float size = 1000f;

    [Range(0, 2048)] public int gridSize = 128;

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
    }

    // Start is called before the first frame update
    void Start()
    {
        GenerateGrid();
    }

    // Update is called once per frame
    void Update()
    {
    }
}