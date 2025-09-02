using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class PrefabHelper
    {
        public static GameObject FindPrefabByName(string name)
        {
            // 这会返回所有已经加载到内存的 GameObject 资源
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            // 按名字匹配
            return all.FirstOrDefault(prefab => prefab.name == name);
        }
    }
}
