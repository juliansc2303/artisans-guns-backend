using UnityEngine;
using UnityEngine.AI;
 
public class MinimapMesh : MonoBehaviour
{
    [SerializeField]
    MeshFilter filter;
    void Awake()
    {
        Bake();
    }
 
    [ContextMenu("Bake Mesh")]
    void Bake()
    {
        // Getnavmesh data
        NavMeshTriangulation triangles = NavMesh.CalculateTriangulation();
        // Create new mesh with data
        Mesh mesh = new Mesh();
        mesh.vertices = triangles.vertices;
        mesh.triangles = triangles.indices;
        // set to mesh filter      
        filter.mesh = mesh;
    }
}