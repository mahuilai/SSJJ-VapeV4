using System;
using System.Collections.Generic;
using System.Reflection;
using physics.data;
using physics.engine.data;
using physics.entity;
using share;
using SSJJMath;
using UnityEngine;
using UnityEngine.Rendering;
using Vape.Cfg;
using Vape.Entity;
using SceneMapRenderer = SSJJSceneLoader.BaseSceneRender;

namespace Vape.Feature.Overlay
{
    public sealed class PhysxModelDisplay : MonoBehaviour
    {
        private sealed class WireGeometry
        {
            public UnityEngine.Object Owner;
            public Bounds Bounds;
            public Mesh LineMesh;
            public int EdgeCount;
            public bool IsSceneMap;
        }

        private sealed class MaterialState
        {
            public Material Material;
            public Shader Shader;
            public Texture MainTexture;
            public Color Color;
            public int RenderQueue;
            public bool HadColor;
        }

        private const int MaxMeshEdges = 500000;
        private const int MaxCachedEdges = 500000;
        private const int MaxDrawEdges = MaxCachedEdges;
        private const int MaxReadableVertices = 60000;
        private const float ScanInterval = 10f;

        private readonly List<WireGeometry> _wires = new List<WireGeometry>(512);
        private readonly HashSet<int> _coveredObjects = new HashSet<int>();
        private readonly Dictionary<int, MaterialState> _mapMaterialStates =
            new Dictionary<int, MaterialState>(128);
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private Material _lineMaterial;
        private Shader _blackMapShader;
        private Camera _mainCamera;
        private float _nextScan;
        private bool _started;
        private bool _wasEnabled;
        private int _cachedEdges;
        private int _lastDrawnEdges = -1;
        private float _nextRenderStatus;
        private FieldInfo _sceneMeshDictionaryField;
        private SceneMapRenderer _sceneRenderOwner;
        private bool _hasFullSceneCache;
        private bool _blackMapApplied;
        private float _nextBlackMapRefresh;
        private bool _skyboxCaptured;
        private Material _savedRenderSettingsSkybox;
        private Camera _blackMapCamera;
        private CameraClearFlags _savedCameraClearFlags;
        private Color _savedCameraBackground;
        private Skybox _cameraSkybox;
        private bool _savedCameraSkyboxEnabled;
        private string _sceneProbeStatus = "SceneMap pending";

        public static string LastStatus { get; private set; } = "Off";
        public int CachedGeometryCount => _wires.Count;
        public int CachedEdgeCount => _cachedEdges;

        private void Awake()
        {
            // AddComponent can invoke Awake from the injector thread. Keep this method
            // completely managed; all Unity native calls are deferred to Start/Update.
        }

        private void Start()
        {
            _started = true;
            _mainCamera = PlayerUpdate.MainCamera ?? Camera.main;
            try
            {
                _sceneMeshDictionaryField = typeof(SceneMapRenderer).GetField("_sceneMeshDict",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch
            {
                _sceneMeshDictionaryField = null;
            }
        }

        private void Update()
        {
            if (!_started)
                return;

            if (!Config.PhysxModel)
            {
                RestoreBlackMapMode();
                if (_wasEnabled)
                    ClearCache();
                _wasEnabled = false;
                LastStatus = "Off";
                return;
            }

            if (!_wasEnabled)
            {
                _wasEnabled = true;
                _nextScan = 0f;
                LastStatus = "Scanning";
            }

            if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
                _mainCamera = PlayerUpdate.MainCamera ?? Camera.main;

            if (_lineMaterial == null)
                EnsureMaterial();

            if (_hasFullSceneCache && _sceneRenderOwner == null)
            {
                RestoreBlackMapMode();
                ClearCache();
                LastStatus = "Map changed - scanning";
            }

            if (Time.unscaledTime >= _nextScan)
                RebuildCache();

            if (Config.PhysxBlackMap)
                ApplyBlackMapMode();
            else
                RestoreBlackMapMode();
        }

        private void OnRenderObject()
        {
            if (!_started || !Config.PhysxModel)
                return;

            Camera renderedCamera = Camera.current;
            if (renderedCamera == null || renderedCamera != _mainCamera ||
                !renderedCamera.isActiveAndEnabled || _wires.Count == 0 || _lineMaterial == null)
                return;

            GeometryUtility.CalculateFrustumPlanes(renderedCamera, _frustumPlanes);
            float range = Mathf.Clamp(Config.PhysxModelDistance, 30f, 250f) * 100f;
            float rangeSquared = range * range;
            Vector3 cameraPosition = renderedCamera.transform.position;

            int drawnEdges = 0;

            try
            {
                Color pink = Vape.UI.Theme.VisualPink;
                Color core = new Color(
                    Mathf.Lerp(pink.r, 1f, 0.32f),
                    Mathf.Lerp(pink.g, 1f, 0.32f),
                    Mathf.Lerp(pink.b, 1f, 0.32f),
                    1f);
                if (_lineMaterial.HasProperty("_Color"))
                    _lineMaterial.SetColor("_Color", core);
                if (!_lineMaterial.SetPass(0))
                    return;

                for (int i = 0; i < _wires.Count && drawnEdges < MaxDrawEdges; i++)
                {
                    WireGeometry wire = _wires[i];
                    if (wire.Owner == null || wire.LineMesh == null)
                        continue;
                    if (!wire.IsSceneMap && wire.Bounds.SqrDistance(cameraPosition) > rangeSquared ||
                        !GeometryUtility.TestPlanesAABB(_frustumPlanes, wire.Bounds))
                        continue;

                    Graphics.DrawMeshNow(wire.LineMesh, Matrix4x4.identity);
                    drawnEdges += wire.EdgeCount;
                }

                if (drawnEdges != _lastDrawnEdges || Time.unscaledTime >= _nextRenderStatus)
                {
                    _lastDrawnEdges = drawnEdges;
                    _nextRenderStatus = Time.unscaledTime + 1f;
                    string mode = _blackMapApplied ? " / black map" : string.Empty;
                    LastStatus = drawnEdges > 0
                        ? (_hasFullSceneCache ? "Full map / " : _wires.Count + " models / ") +
                          drawnEdges + " pink edges" + mode
                        : _wires.Count + " models / no visible edges";
                }

            }
            catch
            {
                LastStatus = "Render retry";
            }
        }

        private bool EnsureMaterial()
        {
            if (_lineMaterial != null)
                return true;

            Shader shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                LastStatus = "Shader unavailable";
                return false;
            }

            _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_Cull", (int)CullMode.Off);
            _lineMaterial.SetInt("_ZWrite", 0);
            _lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            return true;
        }

        private void ApplyBlackMapMode()
        {
            ApplyNoSkybox();
            if (_blackMapApplied && Time.unscaledTime < _nextBlackMapRefresh)
                return;

            _nextBlackMapRefresh = Time.unscaledTime + 0.5f;
            if (_blackMapShader == null)
                _blackMapShader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");

            try
            {
                if (_sceneRenderOwner != null)
                {
                    foreach (Material material in _sceneRenderOwner.GetMaterials())
                        ApplyBlackMaterial(material);
                }

                GameObject mapObject = FindSceneMapObject();
                if (mapObject != null)
                {
                    Renderer[] renderers = mapObject.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Material[] materials = renderers[i]?.sharedMaterials;
                        if (materials == null)
                            continue;
                        for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                            ApplyBlackMaterial(materials[materialIndex]);
                    }
                }
            }
            catch
            {
            }

            _blackMapApplied = true;
        }

        private void ApplyBlackMaterial(Material material)
        {
            if (material == null)
                return;

            int id = material.GetInstanceID();
            if (_mapMaterialStates.ContainsKey(id))
                return;

            bool hadColor = false;
            Color color = Color.white;
            Texture mainTexture = null;
            try
            {
                hadColor = material.HasProperty("_Color");
                if (hadColor)
                    color = material.color;
                mainTexture = material.mainTexture;
            }
            catch
            {
            }

            _mapMaterialStates.Add(id, new MaterialState
            {
                Material = material,
                Shader = material.shader,
                MainTexture = mainTexture,
                Color = color,
                RenderQueue = material.renderQueue,
                HadColor = hadColor
            });

            try
            {
                if (_blackMapShader != null)
                    material.shader = _blackMapShader;
                if (material.HasProperty("_Color"))
                    material.color = Color.black;
                if (material.HasProperty("_MainTex"))
                    material.mainTexture = Texture2D.blackTexture;
                material.renderQueue = 2000;
            }
            catch
            {
            }
        }

        private void ApplyNoSkybox()
        {
            if (!_skyboxCaptured)
            {
                _savedRenderSettingsSkybox = RenderSettings.skybox;
                _skyboxCaptured = true;
            }
            RenderSettings.skybox = null;

            Camera camera = _mainCamera;
            if (camera == null)
                return;
            if (_blackMapCamera != camera)
            {
                RestoreCameraBackground();
                _blackMapCamera = camera;
                _savedCameraClearFlags = camera.clearFlags;
                _savedCameraBackground = camera.backgroundColor;
                _cameraSkybox = camera.GetComponent<Skybox>();
                _savedCameraSkyboxEnabled = _cameraSkybox != null && _cameraSkybox.enabled;
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            if (_cameraSkybox != null)
                _cameraSkybox.enabled = false;
        }

        private void RestoreBlackMapMode()
        {
            if (!_blackMapApplied && !_skyboxCaptured && _mapMaterialStates.Count == 0)
                return;

            foreach (MaterialState state in _mapMaterialStates.Values)
            {
                if (state?.Material == null)
                    continue;
                try
                {
                    state.Material.shader = state.Shader;
                    state.Material.mainTexture = state.MainTexture;
                    if (state.HadColor && state.Material.HasProperty("_Color"))
                        state.Material.color = state.Color;
                    state.Material.renderQueue = state.RenderQueue;
                }
                catch
                {
                }
            }
            _mapMaterialStates.Clear();
            RestoreCameraBackground();
            if (_skyboxCaptured)
                RenderSettings.skybox = _savedRenderSettingsSkybox;
            _savedRenderSettingsSkybox = null;
            _skyboxCaptured = false;
            _blackMapApplied = false;
            _nextBlackMapRefresh = 0f;
        }

        private void RestoreCameraBackground()
        {
            if (_blackMapCamera != null)
            {
                try
                {
                    _blackMapCamera.clearFlags = _savedCameraClearFlags;
                    _blackMapCamera.backgroundColor = _savedCameraBackground;
                    if (_cameraSkybox != null)
                        _cameraSkybox.enabled = _savedCameraSkyboxEnabled;
                }
                catch
                {
                }
            }
            _blackMapCamera = null;
            _cameraSkybox = null;
        }

        private void RebuildCache()
        {
            _nextScan = Time.unscaledTime + ScanInterval;
            _wires.Clear();
            _coveredObjects.Clear();
            _cachedEdges = 0;
            LastStatus = "Scanning";

            int colliderModels = 0;
            int renderModels = 0;
            int nativeModels = 0;
            int sceneMeshes = 0;
            try
            {
                sceneMeshes = AddSceneRenderMeshes();
                if (sceneMeshes == 0)
                {
                    nativeModels = AddNativePhysicsGeometry();
                    colliderModels = AddColliderGeometry();
                    renderModels = AddRendererGeometry();
                }
            }
            catch
            {
                LastStatus = "Scan retry";
                return;
            }

            LastStatus = _wires.Count == 0
                ? _sceneProbeStatus
                : sceneMeshes > 0
                    ? sceneMeshes + " full-map meshes / " + _cachedEdges + " edges"
                    : nativeModels + " native + " + colliderModels + " colliders + " +
                      renderModels + " meshes / " + _cachedEdges + " edges";
            if (_wires.Count == 0)
                _nextScan = Time.unscaledTime + 1f;
            else if (sceneMeshes > 0)
                _nextScan = float.PositiveInfinity;
        }

        private int AddSceneRenderMeshes()
        {
            int before = _wires.Count;
            try
            {
                GameObject mapObject = FindSceneMapObject();
                if (mapObject == null)
                {
                    _sceneProbeStatus = "SceneMap missing";
                    return 0;
                }

                SceneMapRenderer sceneRender = FindSceneRender(mapObject);
                if (sceneRender == null)
                {
                    _sceneProbeStatus = "BaseSceneRender missing";
                    return 0;
                }

                Dictionary<int, Mesh> meshes =
                    _sceneMeshDictionaryField?.GetValue(sceneRender) as Dictionary<int, Mesh>;
                if (meshes == null || meshes.Count == 0)
                {
                    _sceneProbeStatus = _sceneMeshDictionaryField == null
                        ? "Scene mesh field unavailable"
                        : "Scene mesh dictionary empty";
                    return 0;
                }

                SSJJSceneLoader.SceneClipMap clipMap = sceneRender.GetSceneClipMap();
                if (clipMap?.surfaces == null || clipMap.surfaces.Count == 0)
                {
                    _sceneProbeStatus = "Full-map surfaces pending";
                    return 0;
                }

                var edgeSets = new Dictionary<int, HashSet<ulong>>(meshes.Count);
                var edgeLists = new Dictionary<int, List<int>>(meshes.Count);
                int acceptedSurfaces = 0;
                for (int surfaceIndex = 0; surfaceIndex < clipMap.surfaces.Count; surfaceIndex++)
                {
                    SSJJSceneLoader.CSurface surface = clipMap.surfaces[surfaceIndex];
                    if (surface?.surfaceFace?.indexes == null || surface.shader == null ||
                        surface.shader.noRender || !meshes.TryGetValue(surface.meshIndex, out Mesh sourceMesh) ||
                        sourceMesh == null)
                        continue;

                    if (!edgeSets.TryGetValue(surface.meshIndex, out HashSet<ulong> uniqueEdges))
                    {
                        uniqueEdges = new HashSet<ulong>();
                        edgeSets.Add(surface.meshIndex, uniqueEdges);
                        edgeLists.Add(surface.meshIndex, new List<int>());
                    }

                    List<int> sourceEdges = edgeLists[surface.meshIndex];
                    int vertexCount = sourceMesh.vertexCount;
                    var indexes = surface.surfaceFace.indexes;
                    for (int triangle = 0;
                         triangle + 2 < indexes.Count && sourceEdges.Count / 2 < MaxMeshEdges;
                         triangle += 3)
                    {
                        AddUniqueEdge(indexes[triangle], indexes[triangle + 1], vertexCount,
                            uniqueEdges, sourceEdges);
                        AddUniqueEdge(indexes[triangle + 1], indexes[triangle + 2], vertexCount,
                            uniqueEdges, sourceEdges);
                        AddUniqueEdge(indexes[triangle + 2], indexes[triangle], vertexCount,
                            uniqueEdges, sourceEdges);
                    }
                    acceptedSurfaces++;
                }

                _sceneProbeStatus = "Full map: " + acceptedSurfaces + " surfaces";

                foreach (KeyValuePair<int, Mesh> entry in meshes)
                {
                    if (_cachedEdges >= MaxCachedEdges)
                        break;

                    Mesh mesh = entry.Value;
                    if (mesh == null || !mesh.isReadable || mesh.vertexCount <= 0)
                        continue;
                    if (!edgeLists.TryGetValue(entry.Key, out List<int> sourceEdges) ||
                        sourceEdges.Count < 2)
                        continue;

                    Vector3[] vertices;
                    try { vertices = mesh.vertices; }
                    catch { continue; }
                    int[] edges = sourceEdges.ToArray();

                    Bounds bounds = mesh.bounds;
                    if (!IsUsableBounds(bounds))
                        bounds = CalculateBounds(vertices);
                    AddGeometry(sceneRender, bounds, vertices, edges, true);
                }

                _sceneRenderOwner = sceneRender;
                _hasFullSceneCache = _wires.Count > before;
            }
            catch
            {
                _sceneProbeStatus = "Scene mesh read retry";
            }
            return _wires.Count - before;
        }

        private static GameObject FindSceneMapObject()
        {
            try
            {
                GameObject tagged = GameObject.FindGameObjectWithTag("SceneMap");
                if (tagged != null)
                    return tagged;
            }
            catch
            {
            }

            try
            {
                SceneObjectEntity mapEntity = Contexts.sharedInstance?.sceneObject?.sceneMapEntity;
                if (mapEntity != null && mapEntity.hasMapUnityObjects &&
                    mapEntity.mapUnityObjects.MapObject != null)
                    return mapEntity.mapUnityObjects.MapObject;
            }
            catch
            {
            }

            try
            {
                var entities = Contexts.sharedInstance?.sceneObject?.GetEntities();
                if (entities != null)
                {
                    for (int i = 0; i < entities.Count; i++)
                    {
                        SceneObjectEntity entity = entities[i] as SceneObjectEntity;
                        if (entity != null && entity.hasMapUnityObjects &&
                            entity.mapUnityObjects.MapObject != null)
                            return entity.mapUnityObjects.MapObject;
                    }
                }
            }
            catch
            {
            }

            return GameObject.Find("UnitySceneRoot");
        }

        private static SceneMapRenderer FindSceneRender(GameObject mapObject)
        {
            SceneMapRenderer sceneRender = mapObject.GetComponent<SceneMapRenderer>();
            return sceneRender ?? mapObject.GetComponentInChildren<SceneMapRenderer>(true);
        }

        private static Bounds CalculateBounds(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0)
                return default;
            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++)
                bounds.Encapsulate(vertices[i]);
            return bounds;
        }

        private int AddNativePhysicsGeometry()
        {
            int before = _wires.Count;
            try
            {
                var engine = Contexts.sharedInstance?.battleRoom?.pyEngine?.PyEngine;
                IDictionary<int, IPySceneItemEntity> items =
                    engine?.GetWorld()?.GetClipMap()?.GetSceneItemEntityList();
                if (items == null)
                    return 0;

                Vector3 cameraPosition = _mainCamera != null
                    ? _mainCamera.transform.position
                    : Vector3.zero;

                foreach (KeyValuePair<int, IPySceneItemEntity> pair in items)
                {
                    if (_cachedEdges >= MaxCachedEdges)
                        break;

                    IPySceneItemEntity entity = pair.Value;
                    CBrush brush = entity?.GetItemBrush();
                    CollisionBox box = brush?.GetCollisionBox();
                    if (box == null)
                        continue;

                    Vector3 entityPosition = VectorCoordConverter.SsjjToUnity(new Vector3(
                        (float)entity.GetX(), (float)entity.GetY(), (float)entity.GetZ()));

                    Bounds best = default;
                    float bestDistance = float.MaxValue;
                    TrySelectPhysicsBounds(box.GetBoundMins(), box.GetBoundMaxs(), Vector3.zero,
                        cameraPosition, ref best, ref bestDistance);
                    TrySelectPhysicsBounds(box.GetBoundMins(), box.GetBoundMaxs(), entityPosition,
                        cameraPosition, ref best, ref bestDistance);
                    TrySelectPhysicsBounds(box.GetMins(), box.GetMaxs(), Vector3.zero,
                        cameraPosition, ref best, ref bestDistance);
                    TrySelectPhysicsBounds(box.GetMins(), box.GetMaxs(), entityPosition,
                        cameraPosition, ref best, ref bestDistance);

                    if (bestDistance < float.MaxValue)
                        AddBoundsGeometry(this, best);
                }
            }
            catch
            {
            }
            return _wires.Count - before;
        }

        private static void TrySelectPhysicsBounds(Vector3D sourceMin, Vector3D sourceMax,
            Vector3 offset, Vector3 cameraPosition, ref Bounds best, ref float bestDistance)
        {
            if (sourceMin == null || sourceMax == null)
                return;

            Vector3 convertedMin = VectorCoordConverter.SsjjToUnity(sourceMin) + offset;
            Vector3 convertedMax = VectorCoordConverter.SsjjToUnity(sourceMax) + offset;
            Vector3 min = Vector3.Min(convertedMin, convertedMax);
            Vector3 max = Vector3.Max(convertedMin, convertedMax);
            Bounds candidate = new Bounds((min + max) * 0.5f, max - min);
            if (!IsUsableBounds(candidate))
                return;

            float distance = candidate.SqrDistance(cameraPosition);
            if (distance >= bestDistance)
                return;
            best = candidate;
            bestDistance = distance;
        }

        private int AddColliderGeometry()
        {
            int before = _wires.Count;
            Collider[] colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
            for (int i = 0; i < colliders.Length && _cachedEdges < MaxCachedEdges; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy ||
                    IsIgnoredSceneObject(collider.transform))
                    continue;

                Bounds bounds;
                try
                {
                    bounds = collider.bounds;
                }
                catch
                {
                    continue;
                }
                if (!IsUsableBounds(bounds))
                    continue;

                bool added = false;
                MeshCollider meshCollider = collider as MeshCollider;
                if (meshCollider != null && meshCollider.sharedMesh != null)
                    added = TryAddMesh(meshCollider.sharedMesh, collider.transform, collider, bounds);
                if (!added)
                    added = AddBoundsGeometry(collider, bounds);
                if (added)
                    _coveredObjects.Add(collider.gameObject.GetInstanceID());
            }
            return _wires.Count - before;
        }

        private int AddRendererGeometry()
        {
            int before = _wires.Count;
            MeshFilter[] filters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
            for (int i = 0; i < filters.Length && _cachedEdges < MaxCachedEdges; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null || !filter.gameObject.activeInHierarchy ||
                    _coveredObjects.Contains(filter.gameObject.GetInstanceID()) ||
                    IsIgnoredSceneObject(filter.transform))
                    continue;

                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled)
                    continue;

                Bounds bounds;
                try
                {
                    bounds = renderer.bounds;
                }
                catch
                {
                    continue;
                }
                if (!IsUsableBounds(bounds))
                    continue;

                if (TryAddMesh(filter.sharedMesh, filter.transform, filter, bounds) ||
                    AddBoundsGeometry(filter, bounds))
                    _coveredObjects.Add(filter.gameObject.GetInstanceID());
            }
            return _wires.Count - before;
        }

        private bool TryAddMesh(Mesh mesh, Transform transform, UnityEngine.Object owner, Bounds bounds)
        {
            if (mesh == null || transform == null || !mesh.isReadable ||
                mesh.vertexCount <= 0 || mesh.vertexCount > MaxReadableVertices)
                return false;

            int budget = Math.Min(MaxMeshEdges, MaxCachedEdges - _cachedEdges);
            if (!TryBuildMesh(mesh, transform.localToWorldMatrix, budget,
                    out Vector3[] vertices, out int[] edges))
                return false;

            return AddGeometry(owner, bounds, vertices, edges);
        }

        private bool AddGeometry(UnityEngine.Object owner, Bounds bounds, Vector3[] vertices, int[] edges,
            bool isSceneMap = false)
        {
            if (owner == null || vertices == null || vertices.Length == 0 || edges == null || edges.Length < 2)
                return false;

            int remaining = MaxCachedEdges - _cachedEdges;
            if (remaining <= 0)
                return false;
            if (edges.Length / 2 > remaining)
                Array.Resize(ref edges, remaining * 2);

            Mesh lineMesh = null;
            try
            {
                lineMesh = new Mesh
                {
                    name = "vp_physx_lines",
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = vertices.Length > ushort.MaxValue
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                lineMesh.vertices = vertices;
                lineMesh.SetIndices(edges, MeshTopology.Lines, 0, false);
                lineMesh.bounds = bounds;
                lineMesh.UploadMeshData(true);
            }
            catch
            {
                if (lineMesh != null)
                    Destroy(lineMesh);
                return false;
            }

            _wires.Add(new WireGeometry
            {
                Owner = owner,
                Bounds = bounds,
                LineMesh = lineMesh,
                EdgeCount = edges.Length / 2,
                IsSceneMap = isSceneMap
            });
            _cachedEdges += edges.Length / 2;
            return true;
        }

        private bool AddBoundsGeometry(UnityEngine.Object owner, Bounds bounds)
        {
            if (_cachedEdges + 12 > MaxCachedEdges || !IsUsableBounds(bounds))
                return false;
            BuildWorldBounds(bounds, out Vector3[] vertices, out int[] edges);
            return AddGeometry(owner, bounds, vertices, edges);
        }

        private static bool TryBuildMesh(Mesh mesh, Matrix4x4 matrix, int edgeBudget,
            out Vector3[] vertices, out int[] edges)
        {
            vertices = Array.Empty<Vector3>();
            edges = Array.Empty<int>();
            if (mesh == null || edgeBudget <= 0)
                return false;

            Vector3[] sourceVertices;
            int[] triangles;
            try
            {
                sourceVertices = mesh.vertices;
                triangles = mesh.triangles;
            }
            catch
            {
                return false;
            }
            if (sourceVertices == null || sourceVertices.Length == 0 || triangles == null || triangles.Length < 3)
                return false;

            int triangleCount = triangles.Length / 3;
            int stride = Math.Max(1, Mathf.CeilToInt(triangleCount * 3f / edgeBudget));
            var uniqueEdges = new HashSet<ulong>();
            var sourceEdges = new List<int>(Math.Min(edgeBudget * 2, triangles.Length * 2));
            for (int triangle = 0; triangle < triangleCount && sourceEdges.Count / 2 < edgeBudget;
                 triangle += stride)
            {
                int offset = triangle * 3;
                AddUniqueEdge(triangles[offset], triangles[offset + 1], sourceVertices.Length,
                    uniqueEdges, sourceEdges);
                AddUniqueEdge(triangles[offset + 1], triangles[offset + 2], sourceVertices.Length,
                    uniqueEdges, sourceEdges);
                AddUniqueEdge(triangles[offset + 2], triangles[offset], sourceVertices.Length,
                    uniqueEdges, sourceEdges);
            }
            if (sourceEdges.Count == 0)
                return false;

            var remap = new Dictionary<int, int>(sourceEdges.Count);
            var compactVertices = new List<Vector3>(sourceEdges.Count);
            edges = new int[sourceEdges.Count];
            for (int i = 0; i < sourceEdges.Count; i++)
            {
                int sourceIndex = sourceEdges[i];
                if (!remap.TryGetValue(sourceIndex, out int compactIndex))
                {
                    compactIndex = compactVertices.Count;
                    remap.Add(sourceIndex, compactIndex);
                    compactVertices.Add(matrix.MultiplyPoint3x4(sourceVertices[sourceIndex]));
                }
                edges[i] = compactIndex;
            }
            vertices = compactVertices.ToArray();
            return true;
        }

        private static void AddUniqueEdge(int a, int b, int vertexCount, HashSet<ulong> unique,
            List<int> edges)
        {
            if ((uint)a >= vertexCount || (uint)b >= vertexCount || a == b)
                return;
            uint min = (uint)Math.Min(a, b);
            uint max = (uint)Math.Max(a, b);
            if (!unique.Add(((ulong)min << 32) | max))
                return;
            edges.Add(a);
            edges.Add(b);
        }

        private static bool IsIgnoredSceneObject(Transform transform)
        {
            for (int depth = 0; transform != null && depth < 6; depth++, transform = transform.parent)
            {
                string name = transform.name ?? string.Empty;
                if (name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("role", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("canvas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("hud", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("particle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("sky", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.StartsWith("vp_", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsUsableBounds(Bounds bounds)
        {
            Vector3 size = bounds.size;
            return IsFinite(bounds.center) && IsFinite(size) &&
                   size.sqrMagnitude > 0.0001f &&
                   Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < 10000000f;
        }

        private static void BuildWorldBounds(Bounds bounds, out Vector3[] vertices, out int[] edges)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            vertices = new[]
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)
            };
            edges = new[] { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private void ClearCache()
        {
            for (int i = 0; i < _wires.Count; i++)
            {
                Mesh lineMesh = _wires[i]?.LineMesh;
                if (lineMesh != null)
                    Destroy(lineMesh);
            }
            _wires.Clear();
            _coveredObjects.Clear();
            _cachedEdges = 0;
            _nextScan = 0f;
            _lastDrawnEdges = -1;
            _sceneRenderOwner = null;
            _hasFullSceneCache = false;
        }

        private void OnDestroy()
        {
            RestoreBlackMapMode();
            ClearCache();
            if (_lineMaterial != null)
                Destroy(_lineMaterial);
        }

        private void OnDisable()
        {
            RestoreBlackMapMode();
        }
    }
}
