using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowTan.Core.Services
{
    // Типизированная шина: синхронно, в порядке подписки. Ошибка одного обработчика не рвёт остальные.
    public static class EventBus
    {
        const int MaxNesting = 8;

        static readonly Dictionary<Type, List<Delegate>> Handlers = new Dictionary<Type, List<Delegate>>();
        static int depth;

        public static void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            if (!Handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                Handlers.Add(typeof(T), list);
            }

            if (!list.Contains(handler)) list.Add(handler);
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            if (!Handlers.TryGetValue(typeof(T), out var list)) return;
            list.Remove(handler);
            if (list.Count == 0) Handlers.Remove(typeof(T));
        }

        public static void Publish<T>(T evt)
        {
            if (depth >= MaxNesting)
            {
                Debug.LogError($"EventBus: вложенность больше {MaxNesting}, публикация {typeof(T).Name} остановлена");
                return;
            }

            if (!Handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;

            // Снимок: обработчик может отписаться во время рассылки.
            var snapshot = list.ToArray();
            depth++;
            try
            {
                foreach (var handler in snapshot)
                {
                    try
                    {
                        ((Action<T>)handler)(evt);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                depth--;
            }
        }

        public static void Clear()
        {
            Handlers.Clear();
            depth = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Clear();
    }
}
