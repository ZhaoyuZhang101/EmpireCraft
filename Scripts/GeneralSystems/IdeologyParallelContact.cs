using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

public static partial class IdeologyPopulationSystem
{
    private sealed class ContactRequest
    {
        public object World;
        public City City;
        public Kingdom Owner;
        public string Culture;
        public CityPopulationData Data;
        public double At;
        public Actor[] Actors;
        public long[] ActorIds;
        public PopGroup[] Groups;
        public float[] Sizes;
        public int[] Named;
        public SocialClass[] Classes;
        public PartyIdeology[] Ideologies;
        public string[] Cultures, Species;
        public PopulationNumericInput Input;
        public PopulationMathWorkers.Job Job;
    }
    private static readonly Dictionary<City,ContactRequest> PendingContact = new();
    private static bool _contactWavePending, _forceContact;
    public static int ContactWorkerResults { get; private set; }
    public static int ContactFallbackResults { get; private set; }

    private static void ResetParallelContact()
    {
        PendingContact.Clear();_contactWavePending=false;_forceContact=false;
        ContactWorkerResults=ContactFallbackResults=0;
    }

    private static void StartParallelContact(City city,Dictionary<PartyIdeology,int> counts,
        Dictionary<PartyIdeology,int> organizers,HashSet<PartyIdeology> available,
        IReadOnlyDictionary<SocialClass,float> grievances,string culture,ContactSettings settings)
    {
        if(PendingContact.ContainsKey(city))return;
        using var timing=FrameProfiler.Measure("多核理念·取得快照");
        PartyIdeology[] ideas=(PartyIdeology[])Enum.GetValues(typeof(PartyIdeology));
        var options=new ContactOption[ideas.Length];
        var indexes=new Dictionary<PartyIdeology,int>();
        for(int i=0;i<ideas.Length;i++)
        {
            PartyIdeology idea=ideas[i];indexes[idea]=i;int flags=0;
            if(IdeologyFamilies.IsLiberal(idea))flags|=ContactOption.Liberal;
            if(IdeologyFamilies.IsLabourMovement(idea))flags|=ContactOption.Labour;
            if(IdeologyFamilies.IsAgrarianTraditional(idea))flags|=ContactOption.AgrarianTraditional;
            if(IdeologyFamilies.IsAgrarianRadical(idea))flags|=ContactOption.AgrarianRadical;
            if(IdeologyFamilies.IsTraditional(idea))flags|=ContactOption.Traditional;
            if(IdeologyFamilies.IsLeft(idea))flags|=ContactOption.Left;
            if(IdeologyFamilies.IsAuthoritarian(idea))flags|=ContactOption.Authoritarian;
            if(idea is PartyIdeology.SocialLiberalism or PartyIdeology.Centrism or
                PartyIdeology.ConservativeLiberalism or PartyIdeology.Libertarianism)flags|=ContactOption.Civic;
            counts.TryGetValue(idea,out int count);organizers.TryGetValue(idea,out int organized);
            float movement=available.Contains(idea)&&!string.IsNullOrEmpty(culture)&&InstitutionSystem.GetFeature(culture,
                IdeologyInstitutionPaths.StageFeature(idea,1))>0f?IdeologyInstitutionPaths.GetProfile(idea).MovementMultiplier:1f;
            options[i]=new ContactOption((int)idea,flags,count,organized,movement);
        }
        var classes=(SocialClass[])Enum.GetValues(typeof(SocialClass));
        int classCount=classes.Max(c=>(int)c)+1;
        var affinity=new float[classCount,ideas.Length];var grievance=new float[classCount];
        foreach(SocialClass c in classes)
        {
            if(grievances!=null&&grievances.TryGetValue(c,out float value))grievance[(int)c]=value/100f;
            for(int i=0;i<ideas.Length;i++)affinity[(int)c,i]=PartySystem.GetAffinity(ideas[i],c);
        }
        var subjects=new List<ContactSubject>();var actors=new List<Actor>();
        foreach(Actor actor in city.units)
        {
            if(actor?.data==null||actor.isRekt()||!actor.isAlive()||!actor.isAdult())continue;
            var extra=actor.GetOrCreate();SocialClass socialClass=extra.socialClass;
            subjects.Add(new ContactSubject((int)socialClass,(int)Get(actor),actor.getAge(),
                !string.IsNullOrEmpty(extra.last_ideology_social_class)&&extra.last_ideology_social_class!=socialClass.ToString()));
            actors.Add(actor);
        }
        CityPopulationData data=CityPopulationSystem.Get(city);PopGroup[] groups=data.groups.ToArray();
        for(int g=0;g<groups.Length;g++)
        {
            PopGroup group=groups[g];if(group==null)continue;
            float adults=group.Background*.7f;if(adults<1f)continue;
            int samples=Math.Max(1,Math.Min(8,(int)Math.Ceiling(adults)));float amount=adults/samples;
            for(int i=0;i<samples;i++)subjects.Add(new ContactSubject((int)group.social_class,(int)group.ideology,-1,false,g,amount));
        }
        var input=new PopulationNumericInput(new PopulationContactInput(options,available.Select(i=>indexes[i]).ToArray(),
            counts.Keys.Where(available.Contains).Select(i=>indexes[i]).ToArray(),affinity,grievance,subjects.ToArray(),settings,
            (uint)UnityEngine.Random.Range(1,int.MaxValue)));
        var request=new ContactRequest{World=World.world,City=city,Owner=city.kingdom,Culture=culture,Data=data,
            At=World.world.getCurWorldTime(),Actors=actors.ToArray(),ActorIds=actors.Select(a=>a.id).ToArray(),Groups=groups,Input=input,
            Sizes=groups.Select(g=>g?.size??0f).ToArray(),Named=groups.Select(g=>g?.named??0).ToArray(),
            Classes=groups.Select(g=>g?.social_class??default).ToArray(),Ideologies=groups.Select(g=>g?.ideology??default).ToArray(),
            Cultures=groups.Select(g=>g?.culture).ToArray(),Species=groups.Select(g=>g?.species).ToArray()};
        if(_forceContact)
        {ContactFallbackResults++;ApplyContact(request,PopulationMathWorkers.Compute(input).Contact);return;}
        request.Job=PopulationMathWorkers.Submit(input);PendingContact[city]=request;
    }

    private static bool CurrentRequest(ContactRequest r)=>CityPopulationSystem.AbstractPopulationEnabled&&
        ReferenceEquals(r.World,World.world)&&r.City?.data!=null&&!r.City.isRekt()&&r.City.kingdom==r.Owner&&
        World.world.getCurWorldTime()>=r.At&&ReferenceEquals(CityPopulationSystem.Get(r.City),r.Data)&&
        !EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(r.City)&&
        CultureService.GetMainCulture(r.City,initialize:false)==r.Culture;

    private static bool GroupsMatch(ContactRequest r)
    {
        if(r.Groups.Length!=r.Data.groups.Count)return false;
        for(int i=0;i<r.Groups.Length;i++)
        {
            PopGroup g=r.Groups[i];if(!ReferenceEquals(g,r.Data.groups[i]))return false;if(g==null)continue;
            if(g.size!=r.Sizes[i]||g.named!=r.Named[i]||g.social_class!=r.Classes[i]||g.ideology!=r.Ideologies[i]||
                g.culture!=r.Cultures[i]||g.species!=r.Species[i])return false;
        }
        return true;
    }

    private static void CompleteParallelContact(bool flush=false)
    {
        foreach(var pair in PendingContact.ToArray())
        {
            if(!flush&&!SimulationFrameBudget.HasTime)break;
            ContactRequest r=pair.Value;
            try
            {
                if(!CurrentRequest(r)){PendingContact.Remove(pair.Key);continue;}
                if(!flush&&r.Job!=null&&!r.Job.IsCompleted)continue;
                PendingContact.Remove(pair.Key);
                if(!GroupsMatch(r))
                {
                    // 增长、迁移或贸易改信后按当前整座城重算，不能用旧人口覆盖新人口。
                    _forceContact=true;try{ContactCity(r.City);}finally{_forceContact=false;}
                    continue;
                }
                PopulationNumericResult result=null;
                if(r.Job!=null)r.Job.TryGetResult(out result);
                if(result==null){ContactFallbackResults++;result=PopulationMathWorkers.Compute(r.Input);}
                else ContactWorkerResults++;
                ApplyContact(r,result.Contact);
            }
            catch(Exception exception)
            {
                PendingContact.Remove(pair.Key);
                LogService.LogWarning($"[EmpireCraft] 多核理念写回失败({r.City?.data?.name}): {exception.Message}");
            }
        }
    }

    private static void ApplyContact(ContactRequest r,PopulationContactResult result)
    {
        using var timing=FrameProfiler.Measure("多核理念·主线程写回");
        for(int i=0;i<r.Actors.Length;i++)
        {
            Actor actor=r.Actors[i];ContactSubject old=r.Input.Contact.Subjects[i];
            if(actor?.data==null||actor.id!=r.ActorIds[i]||actor.isRekt()||!actor.isAlive()||actor.city!=r.City||!actor.isAdult()||
                (int)actor.GetOrCreate().socialClass!=old.Class||(int)Get(actor)!=old.Current)continue;
            actor.GetOrCreate().last_ideology_social_class=((SocialClass)old.Class).ToString();
            if(result.Targets[i]!=old.Current)Set(actor,(PartyIdeology)result.Targets[i]);
        }
        foreach(ContactMove move in result.Moves)
        {
            PopGroup from=r.Groups[move.Group];float removed=CityPopulationSystem.RemoveBackground(from,move.Amount);
            if(removed>0f)CityPopulationSystem.AddBackground(r.City,from.social_class,from.culture,from.species,
                (PartyIdeology)move.Target,removed);
        }
        if(result.Moves.Length>0)DominantCache.Remove(r.City);
    }

    public static void FlushParallelContact()
    {
        if(!CityPopulationSystem.AbstractPopulationEnabled){ResetParallelContact();return;}
        CompleteParallelContact(true);
    }
}
