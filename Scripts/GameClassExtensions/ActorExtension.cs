using EmpireCraft.Scripts.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class ActorExtension
{
    private static readonly ConditionalWeakTable<Actor, ExtraData> _data
        = new ConditionalWeakTable<Actor, ExtraData>();
    private class ExtraData
    {
        //爵位
        public PeeragesLevel peeragesLevel; 
    }
    public static PeeragesLevel GetPeeragesLevel(this Actor actor)
    {
        return _data.GetOrCreateValue(actor).peeragesLevel;
    }

    public static void SetPeeragesLevel(this Actor actor, PeeragesLevel value)
    {
        _data.GetOrCreateValue(actor).peeragesLevel = value;
    }
}