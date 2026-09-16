using System.Collections.Generic;
using UnityEngine;

public enum BoidBehaviourState
{
    Flocking,
    Arrive,
    Evade,
    Inactive,
    Collected
}
public class BoidAgent : MonoBehaviour
{
    [Header("Local sensors")]
    [SerializeField] private float perceptionRadius = 7.5f;
    [SerializeField] private float separationRadius = 2f;
    [SerializeField] private float hunterVisionRadius = 8f;
    [SerializeField] private float interestSearchRadius = 13f;

    [Header("Movement")]
    [SerializeField] private float maxSpeed = 4.2f;
    [SerializeField] private float acceleration = 6f;
    [SerializeField] private float slowingRadius = 2f;
    [SerializeField] private float separationWeight = 2.2f;
    [SerializeField] private float alignmentWeight = 1f;
    [SerializeField] private float cohesionWeight = 0.85f;
    [SerializeField] private float arriveWeight = 1.65f;
    [SerializeField] private float evadeWeight = 3.5f;

    [Header("Interaction and life")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float interactionInterval = 0.7f;
    [SerializeField] private float interestDamagePerInteraction = 0.5f;
    [SerializeField] private float interestInteractionDistance = 1.35f;
    [SerializeField] private float respawnDelay = 4f;

    private SimulationBootstrap simulation;
    private Renderer[] visualRenderers;
    private Collider bodyCollider;
    private Color baseColor;
    private Vector3 velocity;
    private float health;
    private float interactionTimer;
    private float hitFlashTimer;
    private bool isCollected;
    private InterestObject currentInterest;
    private readonly PhysicsSensorBuffer sensorBuffer = new PhysicsSensorBuffer();
    private readonly HashSet<BoidAgent> sensedBoids = new HashSet<BoidAgent>();
    private readonly HashSet<HunterController> sensedHunters = new HashSet<HunterController>();
    private readonly HashSet<InterestObject> sensedInterests = new HashSet<InterestObject>();
    private BoidBehaviourState behaviourState = BoidBehaviourState.Flocking;

    public Vector3 Velocity => velocity;
    public float Health => health;
    public float MaxHealth => maxHealth;
    public bool IsAlive => health > 0f && !isCollected;
    public bool IsDead => health <= 0f && !isCollected;
    public bool IsCollected => isCollected;
    public BoidBehaviourState BehaviourState => behaviourState;
    public string CurrentBehaviour => behaviourState.ToString();
    public InterestObject CurrentInterest => currentInterest;
    public float HunterDetectionRadius => hunterVisionRadius;

    private void Awake()
    {
        visualRenderers = GetComponentsInChildren<Renderer>();
        bodyCollider = GetComponent<Collider>();
        health = maxHealth;
        baseColor = Color.cyan;
    }

    public void Initialize(SimulationBootstrap owner, int agentIndex, Color color)
    {
        simulation = owner;
        baseColor = color;
        health = maxHealth;
        isCollected = false;
        behaviourState = BoidBehaviourState.Flocking;
        interactionTimer = 0.15f + agentIndex * 0.07f;
        ApplyVisualColor();
    }

    public void SetInitialVelocity(Vector3 initialVelocity)
    {
        initialVelocity.y = 0f;
        velocity = Vector3.ClampMagnitude(initialVelocity, maxSpeed);
    }

    private void Update()
    {
        if (isCollected)
        {
            return;
        }

        if (!IsAlive)
        {
            velocity = Vector3.zero;
            SetBehaviourState(BoidBehaviourState.Inactive);
            return;
        }

        float deltaTime = Time.deltaTime;
        Vector3 desiredVelocity;

        HunterController nearbyHunter = DetectHunter();
        if (nearbyHunter != null)
        {
            currentInterest = null;
            SetBehaviourState(BoidBehaviourState.Evade);

            CalculateFlocking(out Vector3 separation, out _, out _);
            Vector3 evadeDirection = SteeringBehaviours.Evade(
                transform.position,
                nearbyHunter.transform.position,
                nearbyHunter.Velocity,
                1.1f);

            Vector3 desiredDirection = evadeDirection * evadeWeight
                + separation * separationWeight;

            if (desiredDirection.sqrMagnitude < 0.001f)
            {
                desiredDirection = transform.forward;
            }

            desiredVelocity = desiredDirection.normalized * maxSpeed;
        }
        else
        {
            CalculateFlocking(
                out Vector3 separation,
                out Vector3 alignment,
                out Vector3 cohesion);

            currentInterest = FindNearestInterest();
            if (currentInterest != null)
            {
                SetBehaviourState(BoidBehaviourState.Arrive);
                Vector3 offsetToInterest = currentInterest.transform.position - transform.position;
                offsetToInterest.y = 0f;
                Vector3 approachTarget = currentInterest.transform.position;

                if (offsetToInterest.sqrMagnitude > 0.001f)
                {
                    approachTarget -= offsetToInterest.normalized * interestInteractionDistance;
                }

                Vector3 arriveVelocity = SteeringBehaviours.Arrive(
                    transform.position,
                    approachTarget,
                    maxSpeed,
                    slowingRadius);

                Vector3 flockingDirection = separation * separationWeight
                    + alignment * alignmentWeight
                    + cohesion * cohesionWeight;

                desiredVelocity = arriveVelocity * arriveWeight
                    + flockingDirection * maxSpeed;

                InteractWithInterest(currentInterest, deltaTime);
            }
            else
            {
                SetBehaviourState(BoidBehaviourState.Flocking);
                Vector3 flockingDirection = separation * separationWeight
                    + alignment * alignmentWeight
                    + cohesion * cohesionWeight;

                if (flockingDirection.sqrMagnitude < 0.001f)
                {
                    flockingDirection = transform.forward;
                }

                desiredVelocity = flockingDirection.normalized * maxSpeed;
            }
        }

        desiredVelocity.y = 0f;
        velocity = Vector3.MoveTowards(
            velocity,
            Vector3.ClampMagnitude(desiredVelocity, maxSpeed),
            acceleration * deltaTime);

        MoveInsideArena(deltaTime);
        UpdateVisualFeedback(deltaTime);
    }

    private HunterController DetectHunter()
    {
        int colliderCount = sensorBuffer.Query(transform.position, hunterVisionRadius);
        sensedHunters.Clear();
        HunterController closestHunter = null;
        float closestSqrDistance = float.MaxValue;
        float visionSqrRadius = hunterVisionRadius * hunterVisionRadius;

        for (int i = 0; i < colliderCount; i++)
        {
            Collider nearbyCollider = sensorBuffer[i];
            if (nearbyCollider == null)
            {
                continue;
            }

            HunterController hunter = nearbyCollider.GetComponentInParent<HunterController>();
            if (hunter == null || !sensedHunters.Add(hunter))
            {
                continue;
            }

            Vector3 offset = hunter.transform.position - transform.position;
            float sqrDistance = offset.sqrMagnitude;
            if (sqrDistance <= visionSqrRadius && sqrDistance < closestSqrDistance)
            {
                closestHunter = hunter;
                closestSqrDistance = sqrDistance;
            }
        }

        return closestHunter;
    }

    private void CalculateFlocking(
        out Vector3 separation,
        out Vector3 alignment,
        out Vector3 cohesion)
    {
        separation = Vector3.zero;
        alignment = Vector3.zero;
        cohesion = Vector3.zero;

        int colliderCount = sensorBuffer.Query(transform.position, perceptionRadius);
        sensedBoids.Clear();

        for (int i = 0; i < colliderCount; i++)
        {
            Collider nearbyCollider = sensorBuffer[i];
            if (nearbyCollider == null)
            {
                continue;
            }

            BoidAgent neighbour = nearbyCollider.GetComponentInParent<BoidAgent>();
            if (neighbour == null || neighbour == this || !neighbour.IsAlive)
            {
                continue;
            }

            if (!sensedBoids.Add(neighbour))
            {
                continue;
            }

            Vector3 offsetFromNeighbour = transform.position - neighbour.transform.position;
            offsetFromNeighbour.y = 0f;
            float distance = offsetFromNeighbour.magnitude;

            if (distance > 0.01f && distance <= separationRadius)
            {
                separation += offsetFromNeighbour.normalized / distance;
            }

            alignment += SteeringBehaviours.FlatDirection(neighbour.Velocity);
            cohesion += neighbour.transform.position;
        }

        if (sensedBoids.Count == 0)
        {
            return;
        }

        alignment = SteeringBehaviours.FlatDirection(alignment / sensedBoids.Count);
        Vector3 localCentre = cohesion / sensedBoids.Count;
        cohesion = SteeringBehaviours.Seek(transform.position, localCentre);
        separation = SteeringBehaviours.FlatDirection(separation);
    }

    private InterestObject FindNearestInterest()
    {
        int colliderCount = sensorBuffer.Query(transform.position, interestSearchRadius);
        sensedInterests.Clear();
        InterestObject closestInterest = null;
        float closestSqrDistance = float.MaxValue;
        float searchSqrRadius = interestSearchRadius * interestSearchRadius;

        for (int i = 0; i < colliderCount; i++)
        {
            Collider nearbyCollider = sensorBuffer[i];
            if (nearbyCollider == null)
            {
                continue;
            }

            InterestObject interest = nearbyCollider.GetComponentInParent<InterestObject>();
            if (interest == null
                || !interest.IsAvailable
                || !sensedInterests.Add(interest))
            {
                continue;
            }

            Vector3 offset = interest.transform.position - transform.position;
            float sqrDistance = offset.sqrMagnitude;
            if (sqrDistance <= searchSqrRadius && sqrDistance < closestSqrDistance)
            {
                closestInterest = interest;
                closestSqrDistance = sqrDistance;
            }
        }

        return closestInterest;
    }

    private void InteractWithInterest(InterestObject interest, float deltaTime)
    {
        Vector3 offset = interest.transform.position - transform.position;
        if (offset.sqrMagnitude > interestInteractionDistance * interestInteractionDistance)
        {
            return;
        }

        interactionTimer -= deltaTime;
        if (interactionTimer > 0f)
        {
            return;
        }

        interactionTimer = interactionInterval;
        interest.ReceiveDamage(interestDamagePerInteraction);
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive)
        {
            return;
        }

        health = Mathf.Max(0f, health - Mathf.Max(0f, damage));
        hitFlashTimer = 0.22f;

        if (health <= 0f)
        {
            velocity = Vector3.zero;
            currentInterest = null;
            SetBehaviourState(BoidBehaviourState.Inactive);
        }
    }

    public void CollectAndRespawn()
    {
        if (!IsDead || isCollected)
        {
            return;
        }

        isCollected = true;
        velocity = Vector3.zero;
        currentInterest = null;
        SetBehaviourState(BoidBehaviourState.Collected);

        if (bodyCollider != null)
        {
            bodyCollider.enabled = false;
        }

        foreach (Renderer visualRenderer in visualRenderers)
        {
            visualRenderer.enabled = false;
        }

        CancelInvoke(nameof(Respawn));
        Invoke(nameof(Respawn), respawnDelay);
    }

    private void Respawn()
    {
        if (simulation != null)
        {
            transform.position = simulation.GetRandomBoidSpawnPosition();
        }

        health = maxHealth;
        isCollected = false;
        interactionTimer = 0.4f;
        velocity = Random.insideUnitSphere;
        velocity.y = 0f;
        velocity = velocity.normalized * (maxSpeed * 0.7f);

        if (bodyCollider != null)
        {
            bodyCollider.enabled = true;
        }

        foreach (Renderer visualRenderer in visualRenderers)
        {
            visualRenderer.enabled = true;
        }

        SetBehaviourState(BoidBehaviourState.Flocking);
    }

    private void MoveInsideArena(float deltaTime)
    {
        Vector3 position = transform.position + velocity * deltaTime;
        if (simulation != null)
        {
            position = simulation.KeepInsideArena(position, ref velocity);
        }

        KeepSafeDistanceFromInterest(ref position);

        transform.position = position;
        Vector3 flatVelocity = velocity;
        flatVelocity.y = 0f;
        if (flatVelocity.sqrMagnitude > 0.01f)
        {
            transform.forward = Vector3.Slerp(
                transform.forward,
                flatVelocity.normalized,
                8f * deltaTime);
        }
    }

    private void SetBehaviourState(BoidBehaviourState newState)
    {
        if (behaviourState == newState)
        {
            return;
        }

        behaviourState = newState;
        ApplyVisualColor();
    }

    private void UpdateVisualFeedback(float deltaTime)
    {
        if (hitFlashTimer > 0f)
        {
            hitFlashTimer -= deltaTime;
        }

        ApplyVisualColor();
    }

    private void ApplyVisualColor()
    {
        Color stateColor = baseColor;
        switch (behaviourState)
        {
            case BoidBehaviourState.Arrive:
                stateColor = new Color(1f, 0.72f, 0.12f);
                break;
            case BoidBehaviourState.Evade:
                stateColor = new Color(1f, 0.22f, 0.78f);
                break;
            case BoidBehaviourState.Inactive:
                stateColor = new Color(0.28f, 0.28f, 0.32f);
                break;
            case BoidBehaviourState.Collected:
                stateColor = Color.clear;
                break;
        }

        if (hitFlashTimer > 0f)
        {
            stateColor = Color.white;
        }

        foreach (Renderer visualRenderer in visualRenderers)
        {
            if (visualRenderer != null && visualRenderer.enabled)
            {
                visualRenderer.material.color = stateColor;
            }
        }
    }

    private void KeepSafeDistanceFromInterest(ref Vector3 position)
    {
        if (currentInterest == null || !currentInterest.IsAvailable)
        {
            return;
        }

        Vector3 offsetFromInterest = position - currentInterest.transform.position;
        offsetFromInterest.y = 0f;
        float distance = offsetFromInterest.magnitude;

        if (distance < interestInteractionDistance && distance > 0.001f)
        {
            position = currentInterest.transform.position
                + offsetFromInterest.normalized * interestInteractionDistance;
            velocity = Vector3.ProjectOnPlane(velocity, offsetFromInterest.normalized);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.75f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, hunterVisionRadius);
        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, perceptionRadius);
    }
}
