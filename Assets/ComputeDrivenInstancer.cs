using UnityEngine;

public class ComputeDrivenInstancer : MonoBehaviour
{
    public ComputeShader compute;
    public Mesh mesh;
    public Material material;
    public int count = 128;
    private ComputeBuffer positionBuffer;
    private Vector3[] positions;
    private Matrix4x4[] matrices;
    private int kernel;

    void Start()
    {
        positions = new Vector3[count];
        matrices = new Matrix4x4[count];
        for (int i = 0; i < count; i++)
            positions[i] = Random.insideUnitSphere * 5f;

        positionBuffer = new ComputeBuffer(count, sizeof(float) * 3);
        positionBuffer.SetData(positions);
        kernel = compute.FindKernel("CSMain");
        compute.SetBuffer(kernel, "positions", positionBuffer);
    }

    void Update()
    {
        compute.SetFloat("time", Time.time);
        compute.Dispatch(kernel, count / 64 + 1, 1, 1);

        positionBuffer.GetData(positions);

        for (int i = 0; i < count; i++)
            matrices[i] = Matrix4x4.TRS(positions[i], Quaternion.identity, Vector3.one);

        Graphics.DrawMeshInstanced(mesh, 0, material, matrices, count);
    }

    void OnDestroy()
    {
        positionBuffer?.Release();
    }
}
