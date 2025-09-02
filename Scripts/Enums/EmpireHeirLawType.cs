using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Enums
{
    public enum EmpireHeirLawType
    {
        eldest_child, //长嗣
        smallest_child, //幼子
        siblings, //直系同辈
        grand_child_generation, //直系孙辈
        random, //随机
        officer,
        none
    }
}
