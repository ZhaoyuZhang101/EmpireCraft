using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Reflection;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches
{
    // 保护性补丁（Phase 16.1 补充）。
    //
    // 背景：点击"编辑地名历史"打开 CityNameHistoryWindow 时，日志里出现了原版
    // HoveringBgIconManager.animate(WindowAsset) 抛出的 NullReferenceException：
    //   ScrollWindow.showWindow -> ... -> setActive -> setCurrentWindow
    //   -> WindowLibrary 的 post_init 回调 -> HoveringBgIconManager.showWindow
    //   -> HoveringBgIconManager.animate  ×NullReferenceException
    // 这条调用链完全在原版代码里，我们只是通过 ScrollWindow.showWindow 触发了它。
    // 这个异常一旦抛出会把 ScrollWindow.setActive 剩下还没执行完的逻辑一起打断——
    // 包括本该紧接着调用的新窗口 OnNormalEnable/内容构建——于是新窗口虽然确实弹出来
    // 了（标题栏、关闭按钮、上一个/下一个导航都在），但内容区却停在了
    // AutoLayoutWindow<T> 自带的"未找到/开发中"占位页，看起来跟没打开一样。
    // 这不是 CityNameHistoryWindow 或 KingdomTitleWindow 自己代码的问题（两边的
    // OnNormalEnable 已经能正确读到 SelectedMetas/EmpireCraftMetaTypeLibrary 里
    // 的选中对象），而是原版这一小段"悬浮图标飞入动画"逻辑本身有问题，具体是哪个
    // 字段为空目前没有条件反编译原版程序集确认，只能确定是这一步在崩。
    //
    // 用 Harmony 的 Finalizer 补丁把这个异常直接吞掉：animate 只是播放一个装饰性的
    // 悬浮小图标动画，跟窗口真正显示的内容无关，吞掉它顶多是少一个飞入特效，
    // 不会影响任何窗口的功能。这样处理比去猜测触发条件（比如是否跟多选城市有关）
    // 更稳妥，也顺带保护了 KingdomTitleWindow 等所有其它自定义窗口，不用等
    // 每个新窗口都各自踩一遍这个原版的坑。
    public class WindowAnimationStabilityPatch : GamePatch
    {
        public ModDeclare declare { get; set; }

        public void Initialize()
        {
            var harmony = new Harmony("EmpireCraft.WindowAnimationStabilityPatch");
            Type targetType = AccessTools.TypeByName("HoveringBgIconManager");
            if (targetType == null)
            {
                LogService.LogWarning("[EmpireCraft] WindowAnimationStabilityPatch: 未找到 HoveringBgIconManager 类型，跳过补丁。");
                return;
            }

            Type windowAssetType = AccessTools.TypeByName("WindowAsset");
            MethodInfo targetMethod = windowAssetType != null
                ? AccessTools.Method(targetType, "animate", new[] { windowAssetType })
                : null;
            targetMethod ??= AccessTools.Method(targetType, "animate");

            if (targetMethod == null)
            {
                LogService.LogWarning("[EmpireCraft] WindowAnimationStabilityPatch: 未找到 HoveringBgIconManager.animate 方法，跳过补丁。");
                return;
            }

            harmony.Patch(targetMethod,
                finalizer: new HarmonyMethod(typeof(WindowAnimationStabilityPatch), nameof(Finalizer_SwallowException)));

            LogService.LogInfo("[EmpireCraft] WindowAnimationStabilityPatch Initialized");
        }

        // Harmony Finalizer：返回 null 表示"异常已处理，不再向外抛出"，
        // 原方法调用点（也就是原版 setActive 内部那条调用链）会当作正常返回继续走下去，
        // 后面本该执行的新窗口内容构建步骤也就不会再被这里的异常打断了。
        public static Exception Finalizer_SwallowException(Exception __exception)
        {
            if (__exception != null)
            {
                LogService.LogWarning(
                    "[EmpireCraft] 已忽略 HoveringBgIconManager.animate 抛出的异常（不影响窗口内容显示，只是少一个悬浮图标动画）："
                    + __exception);
            }
            return null;
        }
    }
}
