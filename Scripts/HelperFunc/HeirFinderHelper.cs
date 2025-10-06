using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.HelperFunc;

public class HeirFinderHelper
{
    // 继承优先级（与你原脚本一致）
    private ClanRelation[][] s_Priority = new[]
    {
        new[] { ClanRelation.CHILD },
        new[] { ClanRelation.SSGB, ClanRelation.SSGG }
    };

    public HeirFinderHelper(ClanRelation[][] priority = null)
    {
        if (priority != null)
        {
            s_Priority =  priority;
        }
    }
    /// <summary>
    /// 入口1：在调用线程取关系快照，再并行计算（如需，确保此方法在主线程调用）
    /// </summary>
    public async Task<(ClanRelation rel, Actor actor)> FindHeirAsync(
        PersonalClanIdentity personal,
        Action onStart = null,
        CancellationToken ct = default)
    {
        if (personal == null) return (default, null);
        onStart?.Invoke();

        // ⬇️ 整个重活搬到后台线程
        var snapshot = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            // 内部也建议改成“无 LINQ”版本，减少分配
            var list = SpecificClanManager.FindAllRelations(personal);
            return list.ToArray(); // 制作不可变快照
        }, ct).ConfigureAwait(false);

        // 并行/串行筛选（后台线程里做）
        var result = await FindHeirAsyncDir(personal, snapshot, ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// 入口2：已提供关系快照，直接并行计算（最安全，后台无 Unity 访问）
    /// </summary>
    public Task<(ClanRelation, Actor)> FindHeirAsyncDir(
        PersonalClanIdentity personal,
        IReadOnlyList<(ClanRelation rel, PersonalClanIdentity id)> relationsSnapshot,
        CancellationToken ct = default)
    {
        if (personal == null || relationsSnapshot == null || relationsSnapshot.Count == 0)
            return Task.FromResult<(ClanRelation, Actor)>((default, null));

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // 并行收集候选：ConcurrentDictionary<关系, ConcurrentBag<候选>>
            var candidates = new ConcurrentDictionary<ClanRelation, ConcurrentBag<PersonalClanIdentity>>();

            Parallel.ForEach(relationsSnapshot, new ParallelOptions { CancellationToken = ct }, pair =>
            {
                var (rel, id) = pair;
                if (id == null) return;
                if (!id.CanHeir(personal)) return;

                var bag = candidates.GetOrAdd(rel, _ => new ConcurrentBag<PersonalClanIdentity>());
                bag.Add(id);
            });

            // 按优先级顺序挑第一个合法继承人（顺序很重要，这里用串行挑选，保证确定性）
            foreach (var group in s_Priority)
            {
                if (group.Length == 0)
                {
                    // 兜底：任意关系下的 CanHeir
                    foreach (var bag in candidates.Values)
                    {
                        foreach (var heir in bag)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (IsValid(heir)) return (ClanRelation.NONE, heir._actor);
                        }
                    }
                    break;
                }

                foreach (var rel in @group)
                {
                    if (!candidates.TryGetValue(rel, out var bag)) continue;
                    foreach (var heir in bag)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (IsValid(heir)) return (rel, heir._actor);
                    }
                }
            }

            return (default, (Actor)null);
        }, ct);
    }

    // 你的校验逻辑（不要调用 UnityEngine API；如果会，请把校验放回主线程执行）
    private bool IsValid(PersonalClanIdentity id)
    {
        if (id?._actor == null) return false;

        if (id._actor.isRekt()) return false;
        if (!id._actor.isUnitFitToRule() ||
            id._actor.isKing() ||
            id._actor.isCityLeader() ||
            !id._actor.hasClan() ||
            id._actor.isOfficer())
        {
            return false;
        }

        return true;
    }
}