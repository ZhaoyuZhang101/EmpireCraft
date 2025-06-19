using EmpireCraft.Scripts.Enums;
using NeoModLoader.services;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using EpPathFinding.cs;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using NeoModLoader.General;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.TipAndLog;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class ActorExtension
{
    public class ActorExtraData
    {
        public long id;
        // 爵位  
        [JsonConverter(typeof(StringEnumConverter))]
        public PeeragesLevel peeragesLevel;
        public string title = "";
    }

    public static void SetTitle(this Actor a, string value)
    {
        if (a == null || value == null) return;
        if (a.kingdom == null) return;
        if (a.kingdom.GetEmpire() == null) return;
        GetOrCreate(a).title = value + " " + TranslateHelper.GetPeerageTranslate(a.GetPeeragesLevel());
        TranslateHelper.LogPowerfulMinisterAcquireTitle(a, a.kingdom.GetEmpire(), a.GetTitle());
    }
    public static void Clear()
    {
        ExtensionManager<Actor, ActorExtraData>.Clear();
    }

    public static string GetTitle(this Actor a)
    {
        return GetOrCreate(a).title;
    }
    public static bool HasTitle(this Actor a)
    {
        return GetOrCreate(a).title!="" && GetOrCreate(a).title != null;
    }

    public static string GetTileName(this Actor a)
    {
        string title = a.GetTitle();
        string[] titleParts = title.Split(' ');
        if (titleParts.Length <= 0) return null;
        if (titleParts.Length <= 1) return title;
        if (titleParts.Length <= 2) return titleParts[0];
        return titleParts[titleParts.Length - 2];
    }
    public static bool syncData(this Actor a, ActorExtraData actorExtraData)
    {
        var ed = ExtensionManager<Actor, ActorExtraData>.GetOrCreate(a);
        ed.id = actorExtraData.id;
        ed.peeragesLevel = actorExtraData.peeragesLevel;
        ed.title = actorExtraData.title;
        return true;
    }

    public static ActorExtraData getExtraData(this Actor a)
    {
        ActorExtraData data = new ActorExtraData();
        data.id = a.getID();
        data.peeragesLevel = a.GetPeeragesLevel();
        data.title = a.GetTitle();
        return data;
    }

    public static ActorExtraData GetOrCreate(this Actor a)
    {
        var ed = ExtensionManager<Actor, ActorExtraData>.GetOrCreate(a);
        return ed; 
    }
    public static PeeragesLevel GetPeeragesLevel(this Actor a)
        => GetOrCreate(a).peeragesLevel;
    public static void SetPeeragesLevel(this Actor a, PeeragesLevel lvl)
    {
        
        var data = GetOrCreate(a);
        data.id = a.getID();
        data.peeragesLevel = lvl;
    }
}