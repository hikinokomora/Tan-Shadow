using System;
using System.Collections.Generic;
using ShadowTan.Core.Contracts;
using UnityEngine;

namespace ShadowTan.Core.Services
{
    // Лёгкий реестр без DI-фреймворка (ADR-002). Регистрация — только в BootInstaller,
    // исключение — IPlayerQuery: его регистрирует сама героиня.
    public static class Services
    {
        static readonly Dictionary<Type, IService> Registry = new Dictionary<Type, IService>();

        public static void Register<T>(T service) where T : class, IService
        {
            if (service == null)
            {
                Debug.LogError($"Services: попытка зарегистрировать null как {typeof(T).Name}");
                return;
            }

            if (Registry.ContainsKey(typeof(T)))
            {
                Debug.LogError($"Services: {typeof(T).Name} уже зарегистрирован");
                return;
            }

            Registry.Add(typeof(T), service);
        }

        public static T Get<T>() where T : class, IService
        {
            if (Registry.TryGetValue(typeof(T), out var service)) return (T)service;
            throw new InvalidOperationException($"Services: {typeof(T).Name} не зарегистрирован");
        }

        public static bool TryGet<T>(out T service) where T : class, IService
        {
            if (Registry.TryGetValue(typeof(T), out var found))
            {
                service = (T)found;
                return true;
            }

            service = null;
            return false;
        }

        public static void Unregister<T>() where T : class, IService
        {
            Registry.Remove(typeof(T));
        }

        public static void Clear() => Registry.Clear();

        // Без перезагрузки домена статические поля живут между запусками Play Mode (§8.1).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Clear();
    }
}
