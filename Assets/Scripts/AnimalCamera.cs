using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Attach to your Main Camera.
/// A / D  — cycle through individual animals of the current species
/// W / S  — cycle through species
/// Right mouse button drag — orbit camera around current target
/// </summary>
public class AnimalCamera : MonoBehaviour
{
    [Header("Follow")]
    public float followDistance    = 8f;
    public float followHeight      = 3f;
    public float smoothSpeed       = 6f;

    [Header("Orbit")]
    public float orbitSensitivity  = 3f;

    private EntityManager _em;
    private World         _world;

    private System.Collections.Generic.List<System.Collections.Generic.List<Entity>> _species = new();
    private System.Collections.Generic.List<string> _speciesNames = new();

    private int _speciesIndex = 0;
    private int _animalIndex  = 0;

    private float   _orbitYaw   = 0f;
    private float   _orbitPitch = 20f;
    private Vector3 _targetPos;
    private double  _lastRefresh = -1;

    private void Update()
    {
        if (!TryGetWorld()) return;

        // Refresh full list every 2 seconds to catch new spawns
        if (Time.timeAsDouble - _lastRefresh > 2.0)
        {
            RefreshEntities();
            _lastRefresh = Time.timeAsDouble;
        }

        if (_species.Count == 0) return;

        _speciesIndex = Mathf.Clamp(_speciesIndex, 0, _species.Count - 1);

        // ── Purge dead entities from ALL species lists ─────────────────────
        // Done every frame so indices are always valid before any input/follow.
        for (int s = _species.Count - 1; s >= 0; s--)
        {
            var list = _species[s];
            for (int a = list.Count - 1; a >= 0; a--)
            {
                if (!_em.Exists(list[a]) || !_em.HasComponent<LocalTransform>(list[a]))
                {
                    list.RemoveAt(a);
                    // If the removed index was at or below the current animal
                    // index in the active species, pull the index back so we
                    // don't skip past the next alive animal.
                    if (s == _speciesIndex && a <= _animalIndex)
                        _animalIndex = Mathf.Max(0, _animalIndex - 1);
                }
            }
            // Remove the whole species bucket if it's now empty
            if (list.Count == 0)
            {
                _species.RemoveAt(s);
                _speciesNames.RemoveAt(s);
                if (_speciesIndex >= s)
                    _speciesIndex = Mathf.Max(0, _speciesIndex - 1);
            }
        }

        if (_species.Count == 0) return;

        _speciesIndex = Mathf.Clamp(_speciesIndex, 0, _species.Count - 1);
        var current = _species[_speciesIndex];
        if (current.Count == 0) return;
        _animalIndex = Mathf.Clamp(_animalIndex, 0, current.Count - 1);

        // ── Input ──────────────────────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.D)) _animalIndex = (_animalIndex + 1) % current.Count;
        if (Input.GetKeyDown(KeyCode.A)) _animalIndex = (_animalIndex - 1 + current.Count) % current.Count;

        if (Input.GetKeyDown(KeyCode.W)) { _speciesIndex = (_speciesIndex + 1) % _species.Count; _animalIndex = 0; }
        if (Input.GetKeyDown(KeyCode.S)) { _speciesIndex = (_speciesIndex - 1 + _species.Count) % _species.Count; _animalIndex = 0; }

        // ── Orbit ──────────────────────────────────────────────────────────
        if (Input.GetMouseButton(1))
        {
            _orbitYaw   += Input.GetAxis("Mouse X") * orbitSensitivity;
            _orbitPitch -= Input.GetAxis("Mouse Y") * orbitSensitivity;
            _orbitPitch  = Mathf.Clamp(_orbitPitch, 5f, 80f);
        }

        // ── Follow ─────────────────────────────────────────────────────────
        Entity target = current[_animalIndex];
        var lt = _em.GetComponentData<LocalTransform>(target);
        _targetPos = Vector3.Lerp(_targetPos, new Vector3(lt.Position.x, lt.Position.y, lt.Position.z), Time.deltaTime * smoothSpeed);

        float yawRad   = _orbitYaw   * Mathf.Deg2Rad;
        float pitchRad = _orbitPitch * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(
            Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
            Mathf.Sin(pitchRad),
            Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)) * followDistance;
        offset.y += followHeight;

        transform.position = _targetPos + offset;
        transform.LookAt(_targetPos + Vector3.up * 1f);
    }

    private void OnGUI()
    {
        if (_species.Count == 0)
        {
            GUI.Label(new Rect(10, 10, 400, 20), "No animals alive.");
            return;
        }
        string species = _speciesIndex < _speciesNames.Count ? _speciesNames[_speciesIndex] : "?";
        int count = _species[_speciesIndex].Count;
        GUI.Label(new Rect(10, 10, 400, 50),
            $"Species: {species}  [{_animalIndex + 1}/{count}]\nW/S: species   A/D: animal   RMB: orbit");
    }

    private void RefreshEntities()
    {
        // Remember which species/animal we were watching by entity identity
        Entity previousTarget = Entity.Null;
        if (_species.Count > 0 && _speciesIndex < _species.Count)
        {
            var cur = _species[_speciesIndex];
            if (cur.Count > 0 && _animalIndex < cur.Count)
                previousTarget = cur[_animalIndex];
        }

        _species.Clear();
        _speciesNames.Clear();

        if (_world == null || !_world.IsCreated) return;

        var query = _em.CreateEntityQuery(
            ComponentType.ReadOnly<Movement>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FeedingBehavior>());

        using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        using var feedings = query.ToComponentDataArray<FeedingBehavior>(Unity.Collections.Allocator.Temp);

        var herbivores = new System.Collections.Generic.List<Entity>();
        var carnivores = new System.Collections.Generic.List<Entity>();
        var omnivores  = new System.Collections.Generic.List<Entity>();

        for (int i = 0; i < entities.Length; i++)
        {
            var f = feedings[i];
            if      (f.IsHerbivore) herbivores.Add(entities[i]);
            else if (f.IsCarnivore) carnivores.Add(entities[i]);
            else                    omnivores.Add(entities[i]);
        }

        if (herbivores.Count > 0) { _species.Add(herbivores); _speciesNames.Add("Herbivore"); }
        if (carnivores.Count > 0) { _species.Add(carnivores); _speciesNames.Add("Carnivore"); }
        if (omnivores.Count  > 0) { _species.Add(omnivores);  _speciesNames.Add("Omnivore");  }

        query.Dispose();

        // Try to restore the same target we were watching before the refresh
        if (previousTarget != Entity.Null)
        {
            for (int s = 0; s < _species.Count; s++)
            {
                int a = _species[s].IndexOf(previousTarget);
                if (a >= 0) { _speciesIndex = s; _animalIndex = a; return; }
            }
        }

        // Previous target gone — clamp to valid range
        _speciesIndex = Mathf.Clamp(_speciesIndex, 0, Mathf.Max(0, _species.Count - 1));
        if (_species.Count > 0)
            _animalIndex = Mathf.Clamp(_animalIndex, 0, Mathf.Max(0, _species[_speciesIndex].Count - 1));
    }

    private bool TryGetWorld()
    {
        if (_world != null && _world.IsCreated) return true;
        foreach (var w in World.All)
        {
            if (w.IsCreated && w.Name == "Default World")
            {
                _world = w;
                _em    = w.EntityManager;
                return true;
            }
        }
        return false;
    }
}
