using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NCMS.Extensions;
using NeoModLoader.General;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UnityEngine;
using static EmpireCraft.Scripts.HelperFunc.OverallHelperFunc;
using static System.Collections.Specialized.BitVector32;

namespace EmpireCraft.Scripts.AI;
public static class ModPlotsAddition
{
    public static void init()
    {
        AssetManager.plots_library.add(new PlotAsset
        {
            id = "sample_mod_plot", //政策名称
            path_icon = "ChineseCrown.png", //图标位置
            group_id = "diplomacy", //政策群组名称
            is_basic_plot = true, //是否为基础政策
            min_level = 1, //最低等级限制
            money_cost = 30, //政策触发需要消耗的资金
            priority = 999, //优先级
            progress_needed = 60f, //政策触发时长
            can_be_done_by_king = true, //只能被国王触发
            // can_be_done_by_clan_member = true,//只能被氏族成员触发
            // can_be_done_by_leader = true, //只能被城市领导者触发
            check_is_possible = delegate(Actor pActor)
            {
                //todo:政策触发条件
                //满足时返回true, 不满足的时候返回false
                return false;
            },
            action = delegate(Actor pActor)
            {
                //todo:政策执行
                return true;
            },
        });
    }
}
