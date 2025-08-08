using EmpireCraft.Scripts.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class OverallHelperFunc
    {

        public static class IdGenerator
        {
            private static long _lastId = DateTime.UtcNow.Ticks;

            public static long NextId()
            {
                return Interlocked.Increment(ref _lastId);
            }
        }
    }
}
