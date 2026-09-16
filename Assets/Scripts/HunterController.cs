using System.Collections.Generic;
using UnityEngine;


public class HunterController : MonoBehaviour
{
    [Header("Required FSM variables")]
    public float TBA = 3.5f;
    public float RangeAttackRadius = 5.8f;
    public float MeleeAttackRadius = 1.5f;

    [Header("Sensors and movement")]
    [SerializeField] private float visionRadius = 12f;
    [SerializeField] private float patrolSpeed = 3.1f;
    [SerializeField] private float attackSpeed = 6f;
    [SerializeField] private float gatherSpeed = 4.4f;
    [SerializeField] private float waypointReachDistance = 0.8f;
    [SerializeField] private float gatherReachDistance = 1.2f;

    [Header("Attack and Gather")]
    [SerializeField] private float attackDamage = 34f;
    [SerializeField] private float gatherDuration = 1.6f;
    [SerializeField] private float interestSpawnInterval = 5f;
    [SerializeField, Min(0)] private int maxInterestObjects = 5;

    private enum HunterState
    {
        Patrol,
        Attack,
        Gather
    }

    private SimulationBootstrap simulation;
    private List<Transform> waypoints = new List<Transform>();
    private HunterState currentState = HunterState.Patrol;
    private BoidAgent attackTarget;
    private BoidAgent gatherTarget;
    private InterestObject currentObjective;
    private Renderer[] visualRenderers;
    private readonly PhysicsSensorBuffer sensorBuffer = new PhysicsSensorBuffer();
    private readonly HashSet<BoidAgent> sensedBoids = new HashSet<BoidAgent>();
    private Vector3 velocity;
    private float tbaRemaining;
    private float interestSpawnTimer;
    private float elapsedGatherTime;
    private int waypointIndex;
    private int attackCount;
    private int gatherCount;
    private bool stateInitialized;
    private string lastTransition = "Preparando FSM";

    public Vector3 Velocity => velocity;
    public float TBARemaining => tbaRemaining;
    public string CurrentStateName => stateInitialized ? currentState.ToString() : "Sin estado";
    public string CurrentAction { get; private set; } = "Iniciando";
    public string LastTransition => lastTransition;
    public BoidAgent CurrentTarget => attackTarget;
    public InterestObject CurrentObjective => currentObjective;
    public int AttackCount => attackCount;
    public int GatherCount => gatherCount;
    public int CurrentWaypointIndex => waypointIndex;
    public float VisionRadius => visionRadius;
    public int MaxInterestObjects => Mathf.Max(0, maxInterestObjects);

    private void Awake()
    {
        visualRenderers = GetComponentsInChildren<Renderer>();
    }

    public void Initialize(SimulationBootstrap owner, List<Transform> route)
    {
        simulation = owner;
        waypoints = route;
        ResetAttackCooldown();
        interestSpawnTimer = 1.5f;
        waypointIndex = 0;
    }

    private void Start()
    {
        if (simulation == null)
        {
            simulation = FindFirstObjectByType<SimulationBootstrap>();
        }

        ChangeState(HunterState.Patrol);
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        tbaRemaining = Mathf.Max(0f, tbaRemaining - deltaTime);

        if (!stateInitialized)
        {
            ChangeState(HunterState.Patrol);
        }

        switch (currentState)
        {
            case HunterState.Patrol:
                UpdatePatrol(deltaTime);
                break;
            case HunterState.Attack:
                UpdateAttack(deltaTime);
                break;
            case HunterState.Gather:
                UpdateGather(deltaTime);
                break;
        }

        Vector3 movement = velocity;
        movement.y = 0f;
        if (movement.sqrMagnitude > 0.01f)
        {
            transform.forward = Vector3.Slerp(
                transform.forward,
                movement.normalized,
                8f * deltaTime);
        }
    }

    private void ChangeState(HunterState nextState)
    {
        if (stateInitialized && currentState == nextState)
        {
            return;
        }

        string previousState = CurrentStateName;
        if (stateInitialized)
        {
            ExitState(currentState);
        }

        currentState = nextState;
        stateInitialized = true;
        EnterState(nextState);
        lastTransition = string.Format("{0} -> {1}", previousState, currentState);
    }

    private void EnterState(HunterState state)
    {
        switch (state)
        {
            case HunterState.Patrol:
                attackTarget = null;
                gatherTarget = null;
                SetStateColor(new Color(0.95f, 0.18f, 0.12f));
                SetAction("Recorriendo waypoints");
                break;
            case HunterState.Attack:
                SetStateColor(new Color(1f, 0.55f, 0.05f));
                SetAction("Evaluando objetivo");
                break;
            case HunterState.Gather:
                attackTarget = null;
                elapsedGatherTime = 0f;
                SetStateColor(new Color(0.12f, 0.9f, 0.42f));
                SetAction("Gather: dirigiendose al Boid eliminado");
                break;
        }
    }

    private void ExitState(HunterState state)
    {
    }

    private void UpdatePatrol(float deltaTime)
    {
        TrySpawnInterestObject(deltaTime);
        SetAction("Recorriendo waypoints");

        if (TryFindDeadBoidInVision(out BoidAgent deadBoid))
        {
            gatherTarget = deadBoid;
            ChangeState(HunterState.Gather);
            return;
        }

        if (TryFindAliveBoidInVision(out BoidAgent aliveBoid))
        {
            attackTarget = aliveBoid;
            ChangeState(HunterState.Attack);
            return;
        }

        if (!IsTBAReady())
        {
            SetAction(string.Format(
                "Patrol: esperando TBA {0:0.0}s",
                tbaRemaining));
            MoveAlongPatrolRoute(deltaTime);
            return;
        }

        MoveAlongPatrolRoute(deltaTime);
    }

    private void UpdateAttack(float deltaTime)
    {
        BoidAgent target = attackTarget;
        if (target == null || target.IsCollected)
        {
            ChangeState(HunterState.Patrol);
            return;
        }

        if (!target.IsAlive)
        {
            gatherTarget = target;
            ChangeState(HunterState.Gather);
            return;
        }

        if (!IsWithinRadius(target, visionRadius))
        {

            ChangeState(HunterState.Patrol);
            return;
        }

        float sqrDistance = GetSqrDistanceTo(target);
        if (sqrDistance <= MeleeAttackRadius * MeleeAttackRadius)
        {
            StopAndFaceTarget(target.transform.position, deltaTime);
            if (IsTBAReady())
            {
                SetAction("Attack: golpe cuerpo a cuerpo");
                PerformAttack(target, "cuerpo a cuerpo");
            }
            else
            {
                SetAction(string.Format(
                    "Attack: objetivo cuerpo a cuerpo; TBA {0:0.0}s",
                    tbaRemaining));
            }

            return;
        }

        if (sqrDistance <= RangeAttackRadius * RangeAttackRadius)
        {
            StopAndFaceTarget(target.transform.position, deltaTime);
            if (IsTBAReady())
            {
                SetAction("Attack: ataque a distancia");
                PerformAttack(target, "a distancia");
            }
            else
            {
                SetAction(string.Format(
                    "Attack: objetivo fijado; TBA {0:0.0}s",
                    tbaRemaining));
            }

            return;
        }

        SetAction("Attack: persiguiendo al Boid");
        MoveTowardsTarget(target.transform.position, attackSpeed, 1.1f, deltaTime);
    }

    private void UpdateGather(float deltaTime)
    {
        BoidAgent target = gatherTarget;
        if (target == null || target.IsCollected || target.IsAlive)
        {
            ChangeState(HunterState.Patrol);
            return;
        }

        float distance = Vector3.Distance(transform.position, target.transform.position);
        if (distance > gatherReachDistance)
        {
            SetAction("Gather: acercandose al objetivo");
            MoveTowardsTarget(target.transform.position, gatherSpeed, 0.9f, deltaTime);
            return;
        }

        elapsedGatherTime += deltaTime;
        SetAction("Gather: recolectando el objetivo");

        if (elapsedGatherTime >= gatherDuration)
        {
            CompleteGather();
            ChangeState(HunterState.Patrol);
        }
    }

    private void SetStateColor(Color stateColor)
    {
        foreach (Renderer visualRenderer in visualRenderers)
        {
            if (visualRenderer != null)
            {
                visualRenderer.material.color = stateColor;
            }
        }
    }

    private void SetAction(string action)
    {
        CurrentAction = action;
    }

    private bool IsTBAReady()
    {
        return tbaRemaining <= 0f;
    }

    private void ResetAttackCooldown()
    {
        tbaRemaining = Mathf.Max(0f, TBA);
    }

    private bool TryFindAliveBoidInVision(out BoidAgent result)
    {
        result = FindClosestBoidInVision(false);
        return result != null;
    }

    private bool TryFindDeadBoidInVision(out BoidAgent result)
    {
        result = FindClosestBoidInVision(true);
        return result != null;
    }

    private BoidAgent FindClosestBoidInVision(bool deadOnly)
    {
        int colliderCount = sensorBuffer.Query(transform.position, visionRadius);
        sensedBoids.Clear();
        BoidAgent closest = null;
        float closestSqrDistance = float.MaxValue;
        float visionSqrRadius = visionRadius * visionRadius;

        for (int i = 0; i < colliderCount; i++)
        {
            Collider nearbyCollider = sensorBuffer[i];
            BoidAgent boid = nearbyCollider.GetComponentInParent<BoidAgent>();
            if (boid == null || boid.IsCollected || !sensedBoids.Add(boid))
            {
                continue;
            }

            if (deadOnly != boid.IsDead)
            {
                continue;
            }

            float sqrDistance = GetSqrDistanceTo(boid);
            if (sqrDistance > visionSqrRadius)
            {

                continue;
            }

            if (sqrDistance < closestSqrDistance)
            {
                closest = boid;
                closestSqrDistance = sqrDistance;
            }
        }

        return closest;
    }

    public int CountAliveBoidsInVision()
    {
        int colliderCount = sensorBuffer.Query(transform.position, visionRadius);
        sensedBoids.Clear();
        int count = 0;

        for (int i = 0; i < colliderCount; i++)
        {
            Collider nearbyCollider = sensorBuffer[i];
            BoidAgent boid = nearbyCollider.GetComponentInParent<BoidAgent>();
            if (boid != null
                && boid.IsAlive
                && sensedBoids.Add(boid)
                && IsWithinRadius(boid, visionRadius))
            {
                count++;
            }
        }

        return count;
    }

    private bool IsWithinRadius(BoidAgent target, float radius)
    {
        return target != null
            && !target.IsCollected
            && GetSqrDistanceTo(target) <= radius * radius;
    }

    private float GetSqrDistanceTo(BoidAgent target)
    {
        Vector3 offset = target.transform.position - transform.position;
        return offset.sqrMagnitude;
    }

    private void MoveAlongPatrolRoute(float deltaTime)
    {
        if (waypoints == null || waypoints.Count == 0)
        {
            velocity = Vector3.zero;
            return;
        }

        waypointIndex = Mathf.Clamp(waypointIndex, 0, waypoints.Count - 1);
        Transform currentWaypoint = waypoints[waypointIndex];
        if (currentWaypoint == null)
        {
            AdvanceWaypoint();
            return;
        }

        MoveTowardsTarget(currentWaypoint.position, patrolSpeed, 0.8f, deltaTime);

        Vector3 offsetToWaypoint = currentWaypoint.position - transform.position;
        offsetToWaypoint.y = 0f;
        if (offsetToWaypoint.magnitude <= waypointReachDistance)
        {
            AdvanceWaypoint();
        }
    }

    private void AdvanceWaypoint()
    {
        if (waypoints.Count <= 1)
        {
            return;
        }
        waypointIndex = (waypointIndex + 1) % waypoints.Count;
    }

    private void MoveTowardsTarget(
        Vector3 target,
        float speed,
        float slowingRadius,
        float deltaTime)
    {
        Vector3 desiredVelocity = SteeringBehaviours.Arrive(
            transform.position,
            target,
            speed,
            slowingRadius);

        desiredVelocity.y = 0f;
        velocity = desiredVelocity;

        Vector3 nextPosition = transform.position + desiredVelocity * deltaTime;
        if (simulation != null)
        {
            nextPosition = simulation.KeepInsideArena(nextPosition, ref velocity);
        }

        transform.position = nextPosition;
    }

    private void StopAndFaceTarget(Vector3 targetPosition, float deltaTime)
    {
        velocity = Vector3.zero;
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.forward = Vector3.Slerp(
            transform.forward,
            direction.normalized,
            8f * deltaTime);
    }

    private void PerformAttack(BoidAgent target, string attackType)
    {
        if (!IsTBAReady() || target == null || !target.IsAlive)
        {
            return;
        }

        target.TakeDamage(attackDamage);
        ResetAttackCooldown();
        attackCount++;
        SetAction(attackType == "cuerpo a cuerpo"
            ? "Ataque exitoso cuerpo a cuerpo; TBA reiniciado"
            : "Ataque exitoso a distancia; TBA reiniciado");
        Debug.DrawLine(transform.position, target.transform.position, Color.red, 0.35f);

        if (!target.IsAlive)
        {
            gatherTarget = target;
            ChangeState(HunterState.Gather);
            return;
        }

    }

    private void TrySpawnInterestObject(float deltaTime)
    {
        interestSpawnTimer -= deltaTime;
        if (interestSpawnTimer > 0f)
        {
            return;
        }

        interestSpawnTimer = interestSpawnInterval;
        if (simulation == null || simulation.ActiveInterestCount >= MaxInterestObjects)
        {
            return;
        }

        currentObjective = simulation.SpawnInterestObject();
        SetAction("Patrol: genero un objeto de interes");
    }

    private void CompleteGather()
    {
        if (gatherTarget == null || !gatherTarget.IsDead)
        {
            return;
        }

        gatherTarget.CollectAndRespawn();
        gatherCount++;
        SetAction("Gather completado; Boid oculto y reaparicion programada");
        gatherTarget = null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, visionRadius);
        Gizmos.color = new Color(1f, 0.65f, 0.05f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, RangeAttackRadius);
        Gizmos.color = new Color(1f, 0.05f, 0.05f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, MeleeAttackRadius);
    }
}
