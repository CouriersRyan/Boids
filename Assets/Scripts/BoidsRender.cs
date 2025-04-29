using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoidsRender : MonoBehaviour
{
    [SerializeField] private Boids boids;

    [SerializeField] private Mesh instanceMesh;
    [SerializeField] private Material instanceRenderMaterial;
    [SerializeField] public Vector3 boidScale = new Vector3(0.2f, 0.3f, 0.6f);

    private bool _supportInstancing;
    private uint _instanceMeshIndexCount;
    private uint _boidsCount;

    private Bounds _simulationBounds;

    // indices per instance, instance count, start index location, base vertex location, and start index location
    private readonly uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };

    private ComputeBuffer _argsBuffer;

    private static readonly int BoidDataBufferID = Shader.PropertyToID("_BoidDataBuffer");
    private static readonly int ScaleID = Shader.PropertyToID("_BoidScale");

    private void Awake()
    {
        boids = GetComponent<Boids>();
        boids.OnBoidCountChanged += InitValues;
        boids.OnSimulationBoundsChanged += GetSimulationBounds;
    }

    private void Start()
    {
        InitValues();
        GetSimulationBounds();
    }
    
    private void Update()
    {
        if (instanceRenderMaterial == null || boids == null || !_supportInstancing)
        {
            return;
        }

        RenderInstancedMesh();
    }

    private void OnDisable()
    {
        _argsBuffer?.Release();
        boids.OnBoidCountChanged -= InitValues;
        boids.OnSimulationBoundsChanged -= GetSimulationBounds;
    }

    private void InitValues()
    {
        _supportInstancing = SystemInfo.supportsInstancing;
        _instanceMeshIndexCount = (instanceMesh != null ? instanceMesh.GetIndexCount(0) : 0);
        _boidsCount = (uint)boids.BoidsCount;

        _argsBuffer = new ComputeBuffer(1, _args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    private void GetSimulationBounds()
    {
        _simulationBounds = new Bounds(boids.SimulationCenter, boids.SimulationDimensions);
    }
    
    private void RenderInstancedMesh()
    {
        // Update argument buffers
        _args[0] = _instanceMeshIndexCount;
        _args[1] = _boidsCount;
        _argsBuffer.SetData(_args);

        var propertyBlock = new MaterialPropertyBlock();
        
        propertyBlock.SetBuffer(BoidDataBufferID, boids.BoidsDataBuffer);
        propertyBlock.SetVector(ScaleID, boidScale);
        
        Graphics.DrawMeshInstancedIndirect(instanceMesh, 0, instanceRenderMaterial, _simulationBounds,
            _argsBuffer, 0, propertyBlock);
        
    }
}
