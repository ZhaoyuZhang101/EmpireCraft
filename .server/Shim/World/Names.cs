// 起名：一种文化给人、族、城、国各配一套模板(由哪几组字词依次拼成)和各组的字词表，模组照各文化的规矩把它们配好，
// 这里照模板拼出名字。模板里的部件名同模组 OnomasticsType 里的一致。
//# hand T:OnomasticsData
//# hand Culture: getOnomasticData
using System;
using System.Collections.Generic;
using System.Text;

public class OnomasticsData : IDisposable
{
    public const string DEFAULT_NAME_FAILED = "Rebr";
    public const int MAX_NAME_LENGTH = 30;
    public const char SEPARATOR_GROUPS = '|', SEPARATOR_PARTS = ';', SEPARATOR_PART_CONTAINER = ':';
    // 名字里各段(姓、名……)之间的分隔：模组按这个字符拆段
    private const char Divider = '\u200A';
    public Dictionary<string, OnomasticsDataGroup> groups = new();
    private readonly List<string> _template = new();
    private readonly Dictionary<string, string[]> _words = new();
    internal IReadOnlyList<string> ShimTemplate => _template;

    public void clearTemplateData() => _template.Clear();

    public void setTemplateData(IReadOnlyList<string> pTemplateData)
    {
        _template.Clear();
        if (pTemplateData != null) _template.AddRange(pTemplateData);
    }

    public bool setGroup(string pID, string pString)
    {
        if (pID == null) return false;
        groups[pID] = new OnomasticsDataGroup { characters_string = pString ?? "" };
        _words.Remove(pID);
        return true;
    }

    private string[] Words(string group)
    {
        if (_words.TryGetValue(group, out string[] words)) return words;
        words = groups.TryGetValue(group, out OnomasticsDataGroup found) && found?.characters_string != null
            ? found.characters_string.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        return _words[group] = words;
    }

    public string generateName(ActorSex pSex = ActorSex.None, int pCalls = 0, long? pSeed = null)
    {
        var random = pSeed.HasValue ? new Random(unchecked((int)pSeed.Value)) : Random.Shared;
        var name = new StringBuilder();
        for (int i = 0; i < _template.Count; i++)
        {
            string part = _template[i];
            if (part.StartsWith("group_", StringComparison.Ordinal))
            {
                // 跟在一组后面的限定：只对男、只对女、只有一半的机会
                bool use = true;
                while (i + 1 < _template.Count)
                {
                    string next = _template[i + 1];
                    if (next == "sex_male") use &= pSex != ActorSex.Female;
                    else if (next == "sex_female") use &= pSex == ActorSex.Female;
                    else if (next == "coin_flip") use &= random.Next(2) == 0;
                    else break;
                    i++;
                }
                if (!use) continue;
                string[] words = Words(part);
                if (words.Length > 0) name.Append(words[random.Next(words.Length)]);
                continue;
            }
            switch (part)
            {
                case "space" or "divider":
                    if (name.Length > 0 && name[name.Length - 1] != Divider) name.Append(Divider);
                    break;
                case "hyphen" or "consonant_separator":
                    name.Append('-');
                    break;
                case "backspace":
                    if (name.Length > 0) name.Length--;
                    break;
            }
        }
        string result = name.ToString().Trim(Divider, ' ');
        return result.Length > MAX_NAME_LENGTH ? result.Substring(0, MAX_NAME_LENGTH) : result;
    }

    public void Dispose()
    {
    }
}

public partial class Culture
{
    private readonly Dictionary<MetaType, OnomasticsData> _onomastics = new();
    internal Dictionary<MetaType, OnomasticsData> ShimNames => _onomastics;

    public OnomasticsData getOnomasticData(MetaType pType, bool pReset = false)
    {
        if (pReset || !_onomastics.TryGetValue(pType, out OnomasticsData data)) _onomastics[pType] = data = new OnomasticsData();
        return data;
    }
}
