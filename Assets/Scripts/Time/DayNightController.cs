using System;
using Unity.Entities;
using UnityEngine;

[RequireComponent(typeof(Light))]
public class DayNightController : MonoBehaviour
{
    EntityManager? em;
    EntityQuery timeQuery;
    Light sun;

    void Start()
    {
        sun = GetComponent<Light>();
        em = World.DefaultGameObjectInjectionWorld?.EntityManager;

        if (em != null)
        {
            timeQuery = em.Value.CreateEntityQuery(typeof(GameTime));
        }
        else
        {
            Debug.LogWarning("DayNightController: No default World available; time will not update.");
        }
    }

    void Update()
    {
        if (sun == null || em == null)
            return;

        if (timeQuery.IsEmptyIgnoreFilter)
            return; // No GameTime singleton yet

        var gt = timeQuery.GetSingleton<GameTime>();

        // fraction of the current year --> fraction of the day, stable for large values
        double fracDay = gt.TotalYears - Math.Floor(gt.TotalYears);

        // map0-->1 to -90º (sunrise) through270º (next sunrise)
        float angle = (float)(fracDay *360.0 -90.0);
        var newRot = Quaternion.Euler(angle,170f,0f);

        // Avoid setting rotation every frame when change is negligible
        if (Quaternion.Angle(sun.transform.rotation, newRot) >0.01f)
        {
            sun.transform.rotation = newRot;
        }
    }
}