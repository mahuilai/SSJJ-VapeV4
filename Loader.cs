using UnityEngine;

namespace Vape
{
    public class Loader : MonoBehaviour
    {
        private const string HookObjectName = "vp_bootstrap";
        private static GameObject _hookObject;

        public static void Load()
        {
#if Debug_Log
            ConsoleManager.EnsureConsole();
#endif

            if (_hookObject != null)
            {
                EnsureMainComponent(_hookObject);
                return;
            }

            GameObject hookObject = null;
            bool createdObject = false;

            try
            {
                hookObject = GameObject.Find(HookObjectName);
                if (hookObject == null)
                {
                    hookObject = new GameObject(HookObjectName);
                    createdObject = true;
                }

                EnsureMainComponent(hookObject);
                DontDestroyOnLoad(hookObject);
                _hookObject = hookObject;

#if Debug_Log
                global::System.Console.WriteLine($"[Vape] bootstrap ok，ID: {_hookObject.GetInstanceID()}");
                ConsoleManager.WriteColoredNotice();
#endif
            }
            catch (System.Exception ex)
            {
                if (createdObject && hookObject != null)
                {
                    DestroyImmediate(hookObject);
                }

                _hookObject = null;
#if Debug_Log
                global::System.Console.WriteLine($"[Vape] bootstrap failed: {ex}");
#endif
                throw;
            }
        }

        public static void Unload()
        {
            GameObject hookObject = _hookObject ?? GameObject.Find(HookObjectName);
            if (hookObject == null) return;

            DestroyImmediate(hookObject);
            _hookObject = null;
#if Debug_Log
            global::System.Console.WriteLine("[Vape] unloaded");
#endif
        }

        private static void EnsureMainComponent(GameObject hookObject)
        {
            if (hookObject == null)
            {
                throw new MissingReferenceException("Hook object is null");
            }

            if (hookObject.GetComponent<Main>() == null)
            {
                hookObject.AddComponent<Main>();
            }
        }
    }
}

namespace t
{
    public class u : MonoBehaviour
    {
        public static void i()
        {
            Vape.Loader.Load();
        }

        public static void Unload()
        {
            Vape.Loader.Unload();
        }
    }
}
