using UnityEngine;
using UnityEngine.Rendering;

namespace ArtisansGuns.Weapons
{
    /// <summary>
    /// Custom billboard-quad bullet trail.
    /// No TrailRenderer. No LineRenderer.
    ///
    /// Behaviour:
    ///   1. Spawned at fire time: instantly draws a full-length flat quad from
    ///      muzzlePos (firePoint) → impactPos.
    ///   2. Each frame the muzzle end "collapses" toward the impact end at
    ///      shrinkSpeed units/s — giving the impression the trace disappears
    ///      from behind as the bullet travels.
    ///   3. Both endpoints are on FPV layer (6) so the Overlay FPV camera renders
    ///      the trail consistently with the weapon model.
    ///
    /// FPV Sync:
    ///   The muzzle (firePoint) lives in FPV world space.
    ///   The impact lives in base-camera world space.
    ///   If both cameras share position+rotation but have DIFFERENT FOV, the
    ///   impactPos is re-projected through the FPV camera so the trail end lines
    ///   up with where the crosshair impact decal appears on screen.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class BulletTrail : MonoBehaviour
    {
        // ── runtime state ─────────────────────────────────────────────────
        private MeshFilter   _mf;
        private MeshRenderer _mr;
        private Mesh         _mesh;

        private Vector3 _muzzlePos;        // shrinks toward _impactPos after flash
        private Vector3 _muzzleOrigin;     // original muzzle pos (used only for mesh on flash frames)
        private Vector3 _impactPos;        // fixed

        private float   _width;
        private float   _shrinkSpeed;
        private float   _flashTimer;       // counts down before shrink starts
        private Camera  _renderCamera;

        // ── factory ───────────────────────────────────────────────────────
        /// <summary>
        /// Create a bullet trail.
        /// </summary>
        /// <param name="muzzleWorldPos">World-space position of the gun's firePoint.</param>
        /// <param name="impactWorldPos">World-space position of the raycast hit (or max-range point).</param>
        /// <param name="material">Material with additive/transparent shader (e.g. Particle/Unlit additive).</param>
        /// <param name="width">Visual width of the trace quad in world units.</param>
        /// <param name="shrinkSpeed">How fast (units/s) the muzzle end collapses toward impact.</param>
        /// <param name="flashDuration">Seconds the full-length trail stays visible before shrinking.</param>
        /// <param name="baseCam">The main PlayerCamera (used for FOV-sync + billboard).</param>
        /// <param name="fpvCam">Optional FPV Overlay camera.</param>
        public static BulletTrail Create(
            Vector3  muzzleWorldPos,
            Vector3  impactWorldPos,
            Material material,
            float    width,
            float    shrinkSpeed,
            float    flashDuration,
            Camera   baseCam,
            Camera   fpvCam = null)
        {
            var go  = new GameObject("BulletTrail");
            go.layer = 6; // FPV layer — FPV Overlay camera renders it above world geometry,
                          // depth-tested against weapon/hands so they naturally occlude the trail near muzzle

            var bt = go.AddComponent<BulletTrail>();
            bt.Init(muzzleWorldPos, impactWorldPos, material, width, shrinkSpeed, flashDuration, baseCam, fpvCam);
            return bt;
        }

        // ── init ──────────────────────────────────────────────────────────
        private void Init(
            Vector3  muzzleWorldPos,
            Vector3  impactWorldPos,
            Material material,
            float    width,
            float    shrinkSpeed,
            float    flashDuration,
            Camera   baseCam,
            Camera   fpvCam)
        {
            _muzzlePos    = muzzleWorldPos;
            _muzzleOrigin = muzzleWorldPos;
            _shrinkSpeed  = Mathf.Max(shrinkSpeed, 0.1f);
            _width        = Mathf.Max(width, 0.001f);
            _flashTimer   = Mathf.Max(flashDuration, 0f);
            _renderCamera = fpvCam != null ? fpvCam : baseCam; // billboard toward FPV camera

            // Trail lives in world space (Default layer), no FPV remapping needed.
            // The FPV Overlay camera (weapon mesh) composites on top of the base camera
            // output, so the weapon naturally occludes the trail without depth tricks.
            _impactPos = impactWorldPos;

            // Setup mesh components
            _mf = GetComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();

            _mesh = new Mesh { name = "BulletTrailMesh" };
            _mf.mesh = _mesh;

            if (material != null)
            {
                // Use the material as-is (normal depth test).
                // FPV camera only sees layer 6, so world geometry never occludes the trail.
                // The weapon mesh IS on layer 6 and will depth-occlude the trail near the muzzle naturally.
                _mr.material = material;
            }

            _mr.shadowCastingMode = ShadowCastingMode.Off;
            _mr.receiveShadows    = false;
            _mr.allowOcclusionWhenDynamic = false;

            // Build the initial full-length quad immediately (frame 0 = full trail visible)
            RebuildMesh();

            // Safety auto-destroy in case shrink logic stalls
            float length      = Vector3.Distance(_muzzlePos, _impactPos);
            float maxLifetime = (length / _shrinkSpeed) + 0.5f;
            Destroy(gameObject, maxLifetime);
        }

        // ── per-frame shrink ──────────────────────────────────────────────
        private void Update()
        {
            if (_renderCamera == null) { Destroy(gameObject); return; }

            // Flash phase: full line visible, no movement yet
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                // Ensure full-length mesh is drawn (muzzle at origin)
                _muzzlePos = _muzzleOrigin;
                RebuildMesh();
                return;
            }

            // Shrink phase: muzzle collapses toward impact
            _muzzlePos = Vector3.MoveTowards(_muzzlePos, _impactPos, _shrinkSpeed * Time.deltaTime);

            if (Vector3.Distance(_muzzlePos, _impactPos) < 0.005f)
            {
                Destroy(gameObject);
                return;
            }

            RebuildMesh();
        }

        // ── mesh rebuild ──────────────────────────────────────────────────
        private void RebuildMesh()
        {
            Vector3 dir = _impactPos - _muzzlePos;
            if (dir.sqrMagnitude < 0.000001f) return;
            dir.Normalize();

            // Camera-facing perpendicular so the quad always faces the viewer
            Vector3 mid      = (_muzzlePos + _impactPos) * 0.5f;
            Vector3 toCamera = (_renderCamera.transform.position - mid);
            if (toCamera.sqrMagnitude < 0.00001f) return;
            toCamera.Normalize();

            Vector3 perp = Vector3.Cross(dir, toCamera).normalized * (_width * 0.5f);

            // Quad vertices
            // m0/m1 = muzzle side (left/right)  — this side MOVES toward impact
            // i0/i1 = impact side (left/right)   — this side is FIXED
            Vector3 m0 = _muzzlePos - perp;
            Vector3 m1 = _muzzlePos + perp;
            Vector3 i0 = _impactPos - perp;
            Vector3 i1 = _impactPos + perp;

            _mesh.Clear();
            _mesh.vertices = new Vector3[] { m0, m1, i1, i0 };
            _mesh.uv       = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            // Double-sided: front + back triangles
            _mesh.triangles = new int[]
            {
                0, 1, 2,  0, 2, 3,   // front face
                0, 2, 1,  0, 3, 2    // back face
            };
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        // ── FPV sync ──────────────────────────────────────────────────────
        /// <summary>
        /// If the FPV camera has a different FOV than the base camera, the world-space
        /// impact point would appear at a different screen position when rendered by the
        /// FPV Overlay.  Re-project it so the trail end visually aligns with both cameras.
        /// </summary>
        private static Vector3 RemapImpactForFPV(Vector3 impactPos, Camera baseCam, Camera fpvCam)
        {
            if (baseCam == null || fpvCam == null) return impactPos;

            // Same FOV → no remapping needed (common case)
            if (Mathf.Approximately(fpvCam.fieldOfView, baseCam.fieldOfView)) return impactPos;

            // Behind the camera? Don't remap.
            Vector3 camToImpact = impactPos - baseCam.transform.position;
            if (Vector3.Dot(camToImpact, baseCam.transform.forward) <= 0f) return impactPos;

            // Project through base cam → screen
            Vector3 screenPos = baseCam.WorldToScreenPoint(impactPos);
            if (screenPos.z <= 0f) return impactPos;

            // Unproject from FPV cam at the same depth
            float dist    = camToImpact.magnitude;
            Ray   fpvRay  = fpvCam.ScreenPointToRay(screenPos);
            return fpvRay.origin + fpvRay.direction * dist;
        }
    }
}
