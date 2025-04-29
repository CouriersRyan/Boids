using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[System.Serializable]
struct BoidData
{
    public Vector3 velocity;
    public Vector3 position;
}

struct ObstacleData
{
    public float radius;
    public Vector3 position;
}

public delegate void BoidCountChanged();
public delegate void SimulationBoundsChanged();

public class Boids : MonoBehaviour
{
    // Fields
    [Range(128, 60000)]
    [SerializeField] private int boidsCount = 5000;
    private int oldBoidsCounts;

    [Header("Behavior Radii")]
    [SerializeField] private float cohesionRadius = 1.0f; // Radius for cohesion to centroid.
    [SerializeField] private float alignmentRadius = 1.0f; // Radius for aligning to average orientation.
    [SerializeField] private float separationRadius = 0.5f; // Radius for separation to other boids.

    [Header("Behavior Weight")]
    [SerializeField] private float cohesionWeight = 0.5f; // Weight of cohesion force
    [SerializeField] private float alignmentWeight = 0.5f; // Weight of alignment force
    [SerializeField] private float separationWeight = 0.5f; // Weight of separation force

    [Header("Boid")] 
    [SerializeField] private float boidMaxSpeed = 10.0f;
    [SerializeField] private float boidMaxSteeringForce = 1.0f;

    [Header("Obstacles")] 
    [SerializeField] private List<Obstacle> obstacles;
    [SerializeField] private float obstacleAvoidanceWeight = 5f;
    
    [Header("Simulation")]
    [SerializeField] private Vector3 simulationCenter = Vector3.zero;
    [SerializeField] private Vector3 simulationDimensions = new Vector3(32.0f, 32.0f, 32.0f);
    private Vector3 oldSimulationCenter;
    private Vector3 oldSimulationDimensions;
    [SerializeField] private float simulationBoundsAvoidWeight = 10.0f;

    [Header("Compute Shader")]
    [SerializeField] private ComputeShader boidsComputeShader;
    
    private ComputeBuffer _boidsSteeringForcesBuffer; // Buffer for storing steering forces value of boids
    private ComputeBuffer _boidsDataBuffer; // Buffer for storing and passing basic data of boids
    private ComputeBuffer _obstaclesDataBuffer; // Buffer for storing points and radius of obstacles.
    
    private ObstacleData[] _obstacleDatas;
    private bool _obstacleDataUpdated;
    
    private uint _storedThreadGroupsSize; // Group size given by compute shader
    private int _dispatchedThreadGroupSize; // Group size calculated

    private int _steeringForcesKernelId; // Kernel for steering forces calculation
    private int _boidsKernelId; // Kernel for calculating boids basic data

    private const int BoidsDataBufferSize = sizeof(float) * 6;
    private const int ObstacleDataBufferSize = sizeof(float) * 4;
    private const int SteeringsForcesBufferSize = sizeof(float) * 3;

    private static readonly int CountID = Shader.PropertyToID("_BoidsCount");
    private static readonly int ObstaclesCountID = Shader.PropertyToID("_ObstaclesCount");
    private static readonly int BoidsSteeringForcesBufferRwID = Shader.PropertyToID("_BoidsSteeringForcesBufferRw");
    private static readonly int BoidsSteeringForcesBufferID = Shader.PropertyToID("_BoidsSteeringForcesBuffer");
    private static readonly int BoidsDataBufferRwID = Shader.PropertyToID("_BoidsDataBufferRw");
    private static readonly int BoidsDataBufferID = Shader.PropertyToID("_BoidsDataBuffer");
    private static readonly int ObstacleDataBufferID = Shader.PropertyToID("_ObstaclesBuffer");
    private static readonly int CohesionRadiusID = Shader.PropertyToID("_CohesionRadius");
    private static readonly int AlignmentRadiusID = Shader.PropertyToID("_AlignmentRadius");
    private static readonly int SepartionRadiusID = Shader.PropertyToID("_SeparationRadius");
    private static readonly int BoidMaxSpeedID = Shader.PropertyToID("_BoidMaxSpeed");
    private static readonly int BoidMaxSteeringForceID = Shader.PropertyToID("_BoidMaxSteeringForce");
    private static readonly int CohesionWeightID = Shader.PropertyToID("_CohesionWeight");
    private static readonly int AlignmentWeightID = Shader.PropertyToID("_AlignmentWeight");
    private static readonly int SeparationWeightID = Shader.PropertyToID("_SeparationWeight");
    private static readonly int ObstacleAvoidanceWeightID = Shader.PropertyToID("_ObstacleAvoidanceWeight");
    private static readonly int SimulationBoundsAvoidWeightID = Shader.PropertyToID("_SimulationBoundsAvoidWeight");
    private static readonly int SimulationCenterID = Shader.PropertyToID("_SimulationCenter");
    private static readonly int SimulationDimensionsID = Shader.PropertyToID("_SimulationDimensions");
    private static readonly int DeltaTimeID = Shader.PropertyToID("_DeltaTime");

    public event BoidCountChanged OnBoidCountChanged;
    public event SimulationBoundsChanged OnSimulationBoundsChanged;

    // Propeties
    public ComputeBuffer BoidsDataBuffer => _boidsDataBuffer;

    public int BoidsCount => boidsCount;

    public Vector3 SimulationCenter => simulationCenter;

    public Vector3 SimulationDimensions => simulationDimensions;

    // Methods

    private void Start()
    {
        InitBuffers();
        InitKernels();
        oldBoidsCounts = boidsCount; // For knowing when a reload is necessary.
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    private void Update()
    {
        // Call the reload event for boid count changing.
        if (boidsCount != oldBoidsCounts)
        {
            oldBoidsCounts = boidsCount;
            ReleaseBuffers();
            InitBuffers();
            InitKernels();
            OnBoidCountChanged?.Invoke();
        }

        // Call reload event for changing the simulation space.
        if (oldSimulationCenter != simulationCenter || oldSimulationDimensions != simulationDimensions)
        {
            OnSimulationBoundsChanged?.Invoke();
        }
        
        Simulate();
    }

    private void LateUpdate()
    {
        if (_obstacleDataUpdated)
        {
            _obstaclesDataBuffer?.SetData(_obstacleDatas);
            _obstacleDataUpdated = false;
        }
    }

    private void Simulate()
    {
        if(!boidsComputeShader) return;
        
        boidsComputeShader.SetInt(CountID, boidsCount);
        boidsComputeShader.SetInt(ObstaclesCountID, obstacles.Count);
        
        boidsComputeShader.SetBuffer(_steeringForcesKernelId, BoidsDataBufferID, _boidsDataBuffer);
        boidsComputeShader.SetBuffer(_steeringForcesKernelId, BoidsSteeringForcesBufferRwID, _boidsSteeringForcesBuffer);
        boidsComputeShader.SetBuffer(_steeringForcesKernelId, ObstacleDataBufferID, _obstaclesDataBuffer);
        boidsComputeShader.SetBuffer(_boidsKernelId, BoidsSteeringForcesBufferID, _boidsSteeringForcesBuffer);
        boidsComputeShader.SetBuffer(_boidsKernelId, BoidsDataBufferRwID, _boidsDataBuffer);
        
        boidsComputeShader.SetFloat(CohesionRadiusID, cohesionRadius);
        boidsComputeShader.SetFloat(AlignmentRadiusID, alignmentRadius);
        boidsComputeShader.SetFloat(SepartionRadiusID, separationRadius);
        
        boidsComputeShader.SetFloat(BoidMaxSpeedID, boidMaxSpeed);
        boidsComputeShader.SetFloat(BoidMaxSteeringForceID, boidMaxSteeringForce);
        boidsComputeShader.SetFloat(CohesionWeightID, cohesionWeight);
        boidsComputeShader.SetFloat(AlignmentWeightID, alignmentWeight);
        boidsComputeShader.SetFloat(SeparationWeightID, separationWeight);
        boidsComputeShader.SetFloat(ObstacleAvoidanceWeightID, obstacleAvoidanceWeight);
        
        boidsComputeShader.SetFloat(SimulationBoundsAvoidWeightID, simulationBoundsAvoidWeight);
        boidsComputeShader.SetVector(SimulationCenterID, simulationCenter);
        boidsComputeShader.SetVector(SimulationDimensionsID, simulationDimensions);
        boidsComputeShader.SetFloat(DeltaTimeID, Time.deltaTime);
        
        boidsComputeShader.Dispatch(_steeringForcesKernelId, _dispatchedThreadGroupSize, 1, 1);
        boidsComputeShader.Dispatch(_boidsKernelId, _dispatchedThreadGroupSize, 1, 1);
    }

    private void InitBuffers()
    {
        // Set buffer sizes
        _boidsDataBuffer = new ComputeBuffer(boidsCount, BoidsDataBufferSize);
        _boidsSteeringForcesBuffer = new ComputeBuffer(boidsCount, SteeringsForcesBufferSize);
        
        // Prep data arrays
        Vector3[] forces = new Vector3[boidsCount];
        BoidData[] boids = new BoidData[boidsCount];

        for (int i = 0; i < boidsCount; i++)
        {
            forces[i] = Vector3.zero;
            boids[i].position = Random.insideUnitSphere * 1.0f;
            boids[i].velocity = Random.insideUnitSphere * 0.1f;
        }
        
        // Set data arrays to buffers
        _boidsSteeringForcesBuffer.SetData(forces);
        _boidsDataBuffer.SetData(boids);

        // Set up obstacle buffer
        if (obstacles.Count > 0)
        {
            _obstacleDatas = new ObstacleData[obstacles.Count];
            for (int i = 0; i < obstacles.Count; i++)
            {
                _obstacleDatas[i] = new ObstacleData();
                _obstacleDatas[i].position = obstacles[i].Postion;
                _obstacleDatas[i].radius = obstacles[i].Radius;
                obstacles[i].BoidsInstance = this;
                obstacles[i].Index = i;
            }

            _obstaclesDataBuffer = new ComputeBuffer(_obstacleDatas.Length, ObstacleDataBufferSize);
            _obstaclesDataBuffer.SetData(_obstacleDatas);
        }
        else
        {
            _obstaclesDataBuffer = new ComputeBuffer(1, ObstacleDataBufferSize);
        }
    }

    private void ReleaseBuffers()
    {
        SafeReleaseBuffer(ref _boidsDataBuffer);
        SafeReleaseBuffer(ref _boidsSteeringForcesBuffer);
        SafeReleaseBuffer(ref _obstaclesDataBuffer);
    }

    private void SafeReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null) return;
        buffer.Release();
        buffer = null;
    }
    
    private void InitKernels()
    {
        _steeringForcesKernelId = boidsComputeShader.FindKernel("SteeringForcesCS");
        _boidsKernelId = boidsComputeShader.FindKernel("BoidsDataCS");
        
        boidsComputeShader.GetKernelThreadGroupSizes(_steeringForcesKernelId, out _storedThreadGroupsSize, out _, out _);
        var dispatchedThreadGroupSize = boidsCount / (int)_storedThreadGroupsSize;
        
        if(dispatchedThreadGroupSize % _storedThreadGroupsSize == 0) return;

        while (dispatchedThreadGroupSize % _storedThreadGroupsSize != 0)
        {
            dispatchedThreadGroupSize += 1;
        }
        
        _dispatchedThreadGroupSize = dispatchedThreadGroupSize;
        Debug.Log($"Initial threads: {_storedThreadGroupsSize}");
        Debug.Log($"Threads x used: {_dispatchedThreadGroupSize}");
    }

    public void UpdateObstacle(int index, float radius, Vector3 position)
    {
        _obstacleDatas[index].position = position;
        _obstacleDatas[index].radius = radius;
        _obstacleDataUpdated = true;
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireCube(simulationCenter, simulationDimensions);
    }
}
