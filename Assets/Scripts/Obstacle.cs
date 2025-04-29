using System;
using UnityEngine;

public class Obstacle : MonoBehaviour
{
    [SerializeField] private float radius;

    public float Radius => radius;
    public Vector3 Postion 
    {
        get
        {
            return transform.position;
        }
    }

    public Boids BoidsInstance
    {
        get;
        set;
    }

    public int Index
    {
        get;
        set;
    }

    private Vector3 oldPos;
    private float oldRadius;
    
    // Start is called before the first frame update
    void Start()
    {
        oldRadius = radius;
        oldPos = transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        if (oldRadius != radius || oldPos != transform.position)
        {
            // Call event to update data in buffer.
            BoidsInstance.UpdateObstacle(Index, Radius, Postion);
            oldRadius = radius;
            oldPos = transform.position;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
