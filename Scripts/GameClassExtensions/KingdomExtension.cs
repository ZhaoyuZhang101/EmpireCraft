using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using UnityEngine;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class KingdomExtension
{
    private static readonly SemaphoreSlim _sem = new SemaphoreSlim(Environment.ProcessorCount);

    public class KingdomExtraData : ExtraDataBase
    {
        // todo: 添加需要存储的国家数据
    }

    public static KingdomExtraData GetOrCreate(this Kingdom a, bool isSave = false)
    {
        var ed = a.GetOrCreate<Kingdom, KingdomExtraData>(isSave);
        return ed;
    }
}