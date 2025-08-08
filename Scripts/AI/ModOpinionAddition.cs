using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.AI;
public class ModOpinionAddition
{
    public static void init()
    {
        OpinionLibrary opl = AssetManager.opinion_library;
        opl.add(new OpinionAsset
        {
            id = "sample_opinion",
            translation_key = "sample_opinion",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                //todo: 根据不同的条件改变国家之间的态度
                return result;
            }
        });
    }
}