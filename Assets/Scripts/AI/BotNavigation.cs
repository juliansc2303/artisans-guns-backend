using UnityEngine;
using UnityEngine.AI;

namespace ArtisansGuns.AI
{
    /// <summary>
    /// Handles NavMesh-based pathfinding for a bot.
    /// Calculates paths, provides next waypoint, and handles stuck detection.
    /// Does NOT use a NavMeshAgent component — it only queries the NavMesh and
    /// feeds waypoints back to BotBrain, which drives PlayerController movement.
    /// </summary>
    public class BotNavigation
    {
        // ── Path state ──────────────────────────────────────────────────
        private NavMeshPath _path;
        private Vector3[]   _corners;
        private int         _currentCorner;
        private bool        _hasPath;

        // ── Stuck detection ─────────────────────────────────────────────
        private Vector3 _lastPosition;
        private float   _stuckTimer;
        private const float STUCK_THRESHOLD   = 0.3f;  // metres moved
        private const float STUCK_TIME_LIMIT  = 1.5f;  // seconds before declaring stuck

        // ── Patrol / roam ───────────────────────────────────────────────
        private Vector3 _roamCenter;
        private float   _roamRadius;
        private float   _nextRoamTime;

        // ── Anti-oscillation: ring buffer of recent destinations ─────────
        private const int HISTORY_SIZE = 5;
        private const float HISTORY_MIN_DIST = 10f;  // reject candidates within this of recent dests
        private readonly Vector3[] _recentDests = new Vector3[HISTORY_SIZE];
        private int _recentDestsIdx;

        // ── Configuration ───────────────────────────────────────────────
        private readonly float _waypointReachDist;

        public bool HasPath => _hasPath && _corners != null && _currentCorner < _corners.Length;
        public bool IsStuck => _stuckTimer >= STUCK_TIME_LIMIT;
        public Vector3 CurrentTarget => HasPath ? _corners[_corners.Length - 1] : _roamCenter;
        public Vector3 RoamCenter => _roamCenter;

        public BotNavigation(float waypointReachDistance = 1.2f)
        {
            _path = new NavMeshPath();
            _waypointReachDist = waypointReachDistance;
            _lastPosition = Vector3.zero;
        }

        /// <summary>
        /// Set the area where this bot will patrol when not in combat.
        /// </summary>
        public void SetRoamArea(Vector3 center, float radius)
        {
            _roamCenter = center;
            _roamRadius = radius;
            _nextRoamTime = 0f;
        }

        /// <summary>
        /// Calculate a path to the given world-space destination.
        /// Returns true if a valid path was found.
        /// </summary>
        public bool SetDestination(Vector3 from, Vector3 to)
        {
            // Snap both endpoints to NavMesh
            if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, 5f, NavMesh.AllAreas))
                return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, 5f, NavMesh.AllAreas))
                return false;

            _path.ClearCorners();
            if (NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, _path)
                && _path.status != NavMeshPathStatus.PathInvalid)
            {
                _corners = _path.corners;
                _currentCorner = 1; // skip first corner (current position)
                _hasPath = true;
                _stuckTimer = 0f;
                return true;
            }

            _hasPath = false;
            return false;
        }

        /// <summary>
        /// Gets the desired movement direction (normalized, XZ plane) toward the next waypoint.
        /// Call every frame/tick. Returns Vector3.zero when no path or path complete.
        /// </summary>
        public Vector3 GetMoveDirection(Vector3 currentPosition)
        {
            if (!HasPath)
                return Vector3.zero;

            Vector3 target = _corners[_currentCorner];
            Vector3 toTarget = target - currentPosition;
            toTarget.y = 0f; // XZ only

            float dist = toTarget.magnitude;

            if (dist < _waypointReachDist)
            {
                _currentCorner++;
                if (_currentCorner >= _corners.Length)
                {
                    _hasPath = false;
                    return Vector3.zero;
                }
                target = _corners[_currentCorner];
                toTarget = target - currentPosition;
                toTarget.y = 0f;
            }

            // Stuck detection
            UpdateStuckDetection(currentPosition);

            return toTarget.normalized;
        }

        /// <summary>
        /// Pick a random roam destination within the patrol area (if roam timer expired).
        /// Returns true if a new destination was picked, and populates 'destination'.
        /// </summary>
        public bool TryPickRoamDestination(Vector3 currentPosition, out Vector3 destination)
        {
            destination = Vector3.zero;
            if (Time.time < _nextRoamTime)
                return false;

            // Try many candidates; pick the best one that is:
            //  - Far from current position (>10m preferred)
            //  - Far from ALL recent destinations (anti-oscillation)
            //  - On a valid NavMesh path
            Vector3 bestDest = Vector3.zero;
            float bestScore = -1f;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                // Generate candidate: alternate between roam-center and current-position based
                Vector2 rnd = Random.insideUnitCircle * _roamRadius;
                Vector3 basePos;
                if (attempt < 4)
                    basePos = _roamCenter;
                else if (attempt < 7)
                    basePos = currentPosition;
                else
                {
                    // Pure random direction at large distance
                    float angle = Random.Range(0f, 360f);
                    float dist = Random.Range(15f, _roamRadius);
                    rnd = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad)) * dist;
                    basePos = currentPosition;
                }
                Vector3 candidate = basePos + new Vector3(rnd.x, 0f, rnd.y);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, _roamRadius * 0.5f, NavMesh.AllAreas))
                    continue;

                Vector3 pos = hit.position;
                float distFromMe = Vector3.Distance(currentPosition, pos);

                // Reject destinations too close to current position
                if (distFromMe < 6f) continue;

                // Score: distance from me (further = better)
                float score = distFromMe;

                // Penalty: closeness to ANY recent destination (anti-oscillation)
                float minHistDist = float.MaxValue;
                for (int h = 0; h < HISTORY_SIZE; h++)
                {
                    if (_recentDests[h] == Vector3.zero) continue;
                    float hd = Vector3.Distance(pos, _recentDests[h]);
                    if (hd < minHistDist) minHistDist = hd;
                }
                if (minHistDist < HISTORY_MIN_DIST)
                    score -= (HISTORY_MIN_DIST - minHistDist) * 3f;  // heavy penalty

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDest = pos;
                }
            }

            if (bestScore > -100f && bestDest != Vector3.zero)
            {
                // Record in history ring buffer
                _recentDests[_recentDestsIdx % HISTORY_SIZE] = bestDest;
                _recentDestsIdx++;

                destination = bestDest;
                _nextRoamTime = Time.time + Random.Range(0.5f, 2f);
                return true;
            }

            _nextRoamTime = Time.time + 0.3f;
            return false;
        }

        /// <summary>
        /// Forces the bot to abandon its current path and pick a random nearby destination
        /// (used when stuck or fleeing). Tries backward direction first.
        /// </summary>
        public bool Unstick(Vector3 currentPosition)
        {
            _hasPath = false;
            _stuckTimer = 0f;

            // Try backward from last movement direction first, then random
            Vector3 awayDir = (currentPosition - _lastPosition).normalized;
            if (awayDir.sqrMagnitude < 0.01f)
                awayDir = Random.onUnitSphere; // fallback
            awayDir.y = 0f;

            for (int i = 0; i < 8; i++)
            {
                Vector3 dir;
                if (i < 2)
                {
                    // First attempts: go backwards/sideways from stuck direction
                    float angle = (i == 0) ? 180f : (Random.value > 0.5f ? 90f : -90f);
                    dir = Quaternion.Euler(0, angle, 0) * awayDir;
                }
                else
                {
                    dir = Random.insideUnitCircle;
                    dir = new Vector3(dir.x, 0f, dir.y);
                }

                float range = Random.Range(6f, 15f);
                Vector3 candidate = currentPosition + dir.normalized * range;

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    return SetDestination(currentPosition, hit.position);
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the remaining path distance (XZ-plane approximation).
        /// </summary>
        public float RemainingDistance(Vector3 currentPosition)
        {
            if (!HasPath) return 0f;

            float dist = 0f;
            Vector3 prev = currentPosition;
            for (int i = _currentCorner; i < _corners.Length; i++)
            {
                dist += Vector3.Distance(prev, _corners[i]);
                prev = _corners[i];
            }
            return dist;
        }

        // ── Internals ───────────────────────────────────────────────────

        private float _oscillationCheckTimer;
        private Vector3 _oscillationAnchor;  // position when oscillation check started
        private const float OSCILLATION_CHECK_INTERVAL = 4f;  // seconds
        private const float OSCILLATION_RADIUS = 4f;          // if bot stays within this, it's oscillating
        private bool _isOscillating;

        public bool IsOscillating => _isOscillating;

        private void UpdateStuckDetection(Vector3 currentPosition)
        {
            float moved = Vector3.Distance(currentPosition, _lastPosition);
            if (moved < STUCK_THRESHOLD * Time.deltaTime)
                _stuckTimer += Time.deltaTime;
            else
                _stuckTimer = 0f;
            _lastPosition = currentPosition;

            // Oscillation detection: bot is moving (not stuck) but stays in a small area
            _oscillationCheckTimer += Time.deltaTime;
            if (_oscillationCheckTimer >= OSCILLATION_CHECK_INTERVAL)
            {
                float driftFromAnchor = Vector3.Distance(currentPosition, _oscillationAnchor);
                _isOscillating = driftFromAnchor < OSCILLATION_RADIUS;
                _oscillationAnchor = currentPosition;
                _oscillationCheckTimer = 0f;
            }
        }

        /// <summary>Clear path, stuck, and oscillation state.</summary>
        public void Clear()
        {
            _hasPath = false;
            _stuckTimer = 0f;
            _currentCorner = 0;
            _isOscillating = false;
            _oscillationCheckTimer = 0f;
        }
    }
}
