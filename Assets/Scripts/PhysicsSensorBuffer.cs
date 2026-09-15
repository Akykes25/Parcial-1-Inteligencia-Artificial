using System;
using UnityEngine;

/// <summary>
/// Reusable storage for physics perception queries. The buffer grows only
/// when a query fills it, avoiding a new collider array on every frame.
/// </summary>
internal sealed class PhysicsSensorBuffer
{
    private const int DefaultCapacity = 32;

    private Collider[] colliders;

    public PhysicsSensorBuffer(int initialCapacity = DefaultCapacity)
    {
        colliders = new Collider[Mathf.Max(1, initialCapacity)];
    }

    public Collider this[int index] => colliders[index];

    public int Query(Vector3 position, float radius)
    {
        int resultCount = Physics.OverlapSphereNonAlloc(
            position,
            Mathf.Max(0f, radius),
            colliders);

        while (resultCount == colliders.Length)
        {
            Array.Resize(ref colliders, colliders.Length * 2);
            resultCount = Physics.OverlapSphereNonAlloc(
                position,
                Mathf.Max(0f, radius),
                colliders);
        }

        return resultCount;
    }
}
