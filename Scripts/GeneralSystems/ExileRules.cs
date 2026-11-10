namespace EmpireCraft.Scripts.GeneralSystems;

// 流亡政府的判定数(见 ExileGovernmentSystem)；不碰游戏对象，回归测试直接调
public static class ExileRules
{
    // 带走的随员(元首、储君、要员)至多几人
    public const int MaxEntourage = 8;
    // 流亡满这些年还没还都的解散
    public const int ExpireYears = 30;
    // 还都后的收复期：这些年里盟军打回的旧城照样交还
    public const int LiberationYears = 10;
    // 近代国家：科技进了电气时代(第 8 档)
    public const int ModernEraTier = 8;
    // 东道国至少要这么多分：与占领者交战、结盟，或交情很好
    public const int MinHostScore = 2;
    // 交情好到肯收留(好感)
    public const int WarmOpinion = 30;

    // 近代国家才流亡：现代政体、君主立宪，或科技进了电气时代
    public static bool ModernEnough(bool modernRegime, bool constitutional, int eraTier) =>
        modernRegime || constitutional || eraTier >= ModernEraTier;

    // 东道国打分：正与占领者交战 +4，与流亡者同盟 +3，好感很好 +2(好感为正 +1)；
    // 与流亡者交战、站在占领者一边(占领者本身、同一帝国、同盟、傀儡、殖民地)的不收留
    public static int HostScore(bool atWarWithEnemy, bool allied, int opinion, bool hostileToExile, bool sidesWithEnemy)
    {
        if (hostileToExile || sidesWithEnemy) return int.MinValue;
        int score = 0;
        if (atWarWithEnemy) score += 4;
        if (allied) score += 3;
        if (opinion >= WarmOpinion) score += 2;
        else if (opinion > 0) score += 1;
        return score;
    }

    public static bool AcceptableHost(int score) => score >= MinHostScore;

    public static bool Expired(float yearsInExile) => yearsInExile >= ExpireYears;

    public static bool InLiberationWindow(float yearsSinceReturn) => yearsSinceReturn >= 0f && yearsSinceReturn < LiberationYears;

    // 旧城交还给流亡政府：手里拿着它的不是占领者一边、不与流亡政府交战，并且是东道国、盟国或正与占领者交战的
    public static bool Liberator(bool isHost, bool allied, bool atWarWithEnemy, bool sidesWithEnemy, bool hostileToExile) =>
        !sidesWithEnemy && !hostileToExile && (isHost || allied || atWarWithEnemy);
}
