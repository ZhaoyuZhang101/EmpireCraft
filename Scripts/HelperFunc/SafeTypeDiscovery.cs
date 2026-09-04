using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class SafeTypeDiscovery
    {
        public static IEnumerable<Type> GetConcreteDerivedTypes(Type baseType,
            IEnumerable<Assembly> assemblies, Action<string> warn = null)
        {
            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException error)
                {
                    types = error.Types;
                    warn?.Invoke("Type scan partially loaded " + assembly.FullName + ": " + error.Message);
                }
                catch (Exception error) when (IsLoadFailure(error))
                {
                    warn?.Invoke("Type scan skipped " + assembly.FullName + ": " + error.Message);
                    continue;
                }

                bool reported = false;
                foreach (Type type in types ?? Array.Empty<Type>())
                {
                    bool matches;
                    try
                    {
                        // Mono may return a Type whose parent still fails to load in IsAssignableFrom.
                        matches = type != null && !type.IsAbstract && !type.ContainsGenericParameters &&
                                  baseType.IsAssignableFrom(type);
                    }
                    catch (Exception error) when (IsLoadFailure(error))
                    {
                        if (!reported)
                        {
                            reported = true;
                            warn?.Invoke("Type scan skipped unloadable types in " + assembly.FullName + ": " + error.Message);
                        }
                        continue;
                    }
                    if (matches) yield return type;
                }
            }
        }

        private static bool IsLoadFailure(Exception error)
        {
            return error is TypeLoadException || error is ReflectionTypeLoadException ||
                   error is FileNotFoundException || error is FileLoadException ||
                   error is BadImageFormatException || error is NotSupportedException;
        }
    }
}
