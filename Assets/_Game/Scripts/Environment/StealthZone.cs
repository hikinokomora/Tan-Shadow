using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Environment
{
    public enum StealthZoneKind
    {
        Cover, // за занавесом: не видно, пока стражник не подойдёт вплотную
        Shadow // в тени: видно только вблизи
    }

    // Зона-триггер для стелса. Вешается на коллайдер с isTrigger (занавес, тень под навесом).
    [RequireComponent(typeof(Collider))]
    public class StealthZone : MonoBehaviour
    {
        public StealthZoneKind kind = StealthZoneKind.Cover;

        static readonly Collider[] overlap = new Collider[16];

        void Reset() => GetComponent<Collider>().isTrigger = true;

        // Какие зоны накрывают точку. Cover важнее Shadow.
        public static bool TryGetZone(Vector3 point, out StealthZoneKind kind)
        {
            kind = StealthZoneKind.Shadow;
            bool found = false;
            int count = Physics.OverlapSphereNonAlloc(point, 0.1f, overlap, Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var zone = overlap[i].GetComponent<StealthZone>();
                if (zone == null) continue;
                found = true;
                if (zone.kind == StealthZoneKind.Cover)
                {
                    kind = StealthZoneKind.Cover;
                    return true;
                }
            }
            return found;
        }
    }
}
