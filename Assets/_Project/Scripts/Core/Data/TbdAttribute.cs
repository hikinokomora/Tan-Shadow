using UnityEngine;

namespace ShadowTan.Core.Data
{
    // Пометка «значение не утверждено»: источник — GDD или открытый вопрос (§29.2).
    public sealed class TbdAttribute : PropertyAttribute
    {
        public readonly string Source;

        public TbdAttribute(string source = null)
        {
            Source = source;
        }
    }
}
