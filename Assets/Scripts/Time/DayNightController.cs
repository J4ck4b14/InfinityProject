using Unity.Entities;
using UnityEngine;

[RequireComponent(typeof(Light))]
public class DayNightController : MonoBehaviour
{
    EntityManager em;
    Entity timeEnt;
    Light sun;

    void Start()
    {
        sun = GetComponent<Light>();
        em = World.DefaultGameObjectInjectionWorld.EntityManager;
        timeEnt = em.CreateEntityQuery(typeof(GameTime)).GetSingletonEntity();
    }

    void Update()
    {
        var gt = em.GetComponentData<GameTime>(timeEnt);

        // fraction of the current year --> fraction of the day
        double fracDay = gt.TotalYears % 1.0;
        // map 0-->1 to -90° (sunrise) through 270° (next sunrise)
        float angle = (float)(fracDay * 360.0 - 90.0);
        sun.transform.rotation = Quaternion.Euler(angle, 170f, 0f);
    }
}