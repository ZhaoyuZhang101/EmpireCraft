using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.HelperFunc;

// 纯数值模型。选项 ID、阶层 ID 和类别标志由主线程复制，不读取游戏资产或 Unity 随机数。
public readonly struct ContactOption
{
    public const int Liberal=1, Labour=2, AgrarianTraditional=4, AgrarianRadical=8,
        Traditional=16, Left=32, Authoritarian=64, Civic=128;
    public readonly int Id, Flags, Count, Organizers;
    public readonly float Movement;
    public ContactOption(int id,int flags,int count,int organizers,float movement)
    { Id=id;Flags=flags;Count=count;Organizers=organizers;Movement=movement; }
    public bool Has(int flag)=>(Flags & flag)!=0;
}

public readonly struct ContactSubject
{
    public readonly int Class, Current, Age, Group;
    public readonly bool ClassChanged;
    public readonly float Amount;
    public ContactSubject(int socialClass,int current,int age,bool changed,int group=-1,float amount=0)
    { Class=socialClass;Current=current;Age=age;ClassChanged=changed;Group=group;Amount=amount; }
}

public struct ContactSettings
{
    public int Foreign, Pressured, Founding, Governing, Conservative, Peasant, Labour, Citizen;
    public bool Empire, ForeignCity, Revolutionary;
    public float Pressure, External, Fatigue, Trend, Liberation, Decay, Prosperity, Industry, Settled,
        Landless, Pluralism, GrowthThreshold, DeclineThreshold;
}

public sealed class PopulationContactInput
{
    public readonly ContactOption[] Options;
    public readonly int[] Available, Local;
    public readonly float[,] Affinities;
    public readonly float[] Grievances;
    public readonly ContactSubject[] Subjects;
    public readonly ContactSettings Settings;
    public readonly uint Seed;
    public PopulationContactInput(ContactOption[] options,int[] available,int[] local,float[,] affinities,
        float[] grievances,ContactSubject[] subjects,ContactSettings settings,uint seed)
    {
        Options=(ContactOption[])options.Clone();Available=(int[])available.Clone();Local=(int[])local.Clone();
        Affinities=(float[,])affinities.Clone();Grievances=(float[])grievances.Clone();
        Subjects=(ContactSubject[])subjects.Clone();Settings=settings;Seed=seed;
    }
    public PopulationContactInput Copy()=>new(Options,Available,Local,Affinities,Grievances,Subjects,Settings,Seed);
}

public struct ContactRandom
{
    private uint _state;
    public ContactRandom(uint seed) { _state=seed==0?1u:seed; }
    public float Value()
    {
        uint x=_state;x^=x<<13;x^=x>>17;x^=x<<5;_state=x;
        return (x >> 8) * (1f / 16777216f);
    }
    public int Range(int min,int max)=>min+(int)(Value()*(max-min));
}

public readonly struct ContactMove
{
    public readonly int Group, Target;
    public readonly float Amount;
    public ContactMove(int group,int target,float amount){Group=group;Target=target;Amount=amount;}
}

public sealed class PopulationContactResult
{
    public int[] Targets;
    public ContactMove[] Moves;
}

public static class PopulationContactMath
{
    private static float Clamp(float v)=>Math.Max(0f,Math.Min(1f,v));
    public static PopulationContactResult Compute(PopulationContactInput input)
    {
        var s=input.Settings;
        int classes=input.Affinities.GetLength(0),n=input.Options.Length;
        var initial=new float[classes,n];var standard=new float[classes,n];
        var response=new float[classes,n];var local=new float[classes,n];
        var liberal=Filter(ContactOption.Liberal);var socialist=Filter(ContactOption.Labour);
        var traditional=Filter(ContactOption.AgrarianTraditional);var radical=Filter(ContactOption.AgrarianRadical);
        var other=Filter(ContactOption.Liberal,true);
        for(int c=0;c<classes;c++)for(int i=0;i<n;i++)
        {
            ContactOption o=input.Options[i];float affinity=input.Affinities[c,i];
            initial[c,i]=Math.Max(1f,affinity+25f);standard[c,i]=Math.Max(1f,affinity+35f);
            float multiplier=1f;
            if((c==s.Peasant||c==s.Labour)&&o.Has(ContactOption.Left))multiplier+=1.5f*input.Grievances[c];
            if(c==s.Citizen&&o.Has(ContactOption.Civic))multiplier+=.8f;
            if(s.Revolutionary&&(c==s.Peasant||c==s.Labour)&&o.Has(ContactOption.Left))multiplier+=2.5f;
            response[c,i]=standard[c,i]*Math.Max(0f,multiplier);
            local[c,i]=o.Count*(.65f+.7f*Clamp((affinity+100f)/200f))+o.Organizers*2f;
        }
        var random=new ContactRandom(input.Seed);
        var result=new PopulationContactResult{Targets=new int[input.Subjects.Length]};
        var moves=new List<ContactMove>();
        for(int i=0;i<input.Subjects.Length;i++)
        {
            ContactSubject person=input.Subjects[i];
            int age=person.Age<0?random.Range(16,70):person.Age;
            int target=Decide(person,age);
            result.Targets[i]=target;
            if(person.Group>=0&&target!=person.Current)moves.Add(new ContactMove(person.Group,target,person.Amount));
        }
        result.Moves=moves.ToArray();return result;

        int[] Filter(int flag,bool invert=false)
        {
            var choices=new List<int>();foreach(int i in input.Available)
                if(input.Options[i].Has(flag)!=invert)choices.Add(i);
            return choices.ToArray();
        }
        int Pick(int[] choices,float[,] weights,int c,bool localSum=false)
        {
            if(choices.Length==0)return s.Conservative;
            float total=0f;
            if(localSum){foreach(int i in choices)total+=weights[c,i];}
            else{double sum=0;foreach(int i in choices)sum+=weights[c,i];total=(float)sum;}
            float roll=random.Value()*total;
            foreach(int i in choices)if((roll-=weights[c,i])<=0f)return input.Options[i].Id;
            return input.Options[choices[localSum?0:choices.Length-1]].Id;
        }
        int Index(int id){for(int i=0;i<n;i++)if(input.Options[i].Id==id)return i;return -1;}
        bool Available(int id){foreach(int i in input.Available)if(input.Options[i].Id==id)return true;return false;}
        int Decide(ContactSubject person,int age)
        {
            int c=person.Class,current=person.Current;
            if(c<0||c>=classes||Index(current)<0)return current;
            float grievance=input.Grievances[c],contact=random.Value();
            bool classResponse=random.Value()<.15f+.25f*grievance;
            int target=classResponse?Pick(input.Available,response,c)
                :contact<.15f?Pick(input.Available,initial,c)
                :contact<.3f?(s.Pressured>=0&&Available(s.Pressured)&&contact<.24f?s.Pressured
                    :Available(s.Foreign)?s.Foreign:Pick(input.Available,initial,c))
                :s.Liberation>0f&&random.Value()<.5f*s.Liberation?Pick(input.Available,initial,c)
                :input.Local.Length==0?Pick(input.Available,initial,c):Pick(input.Local,local,c,true);
            if(other.Length>0&&random.Value()<s.Pluralism*.25f)target=input.Options[other[random.Range(0,other.Length)]].Id;
            else if(liberal.Length>0&&random.Value()<s.Prosperity*.2f)target=Pick(liberal,standard,c);
            else if(socialist.Length>0&&random.Value()<s.Industry*.2f)target=Pick(socialist,standard,c);
            else if(radical.Length>0&&random.Value()<s.Landless*.2f)target=Pick(radical,standard,c);
            else if(traditional.Length>0&&random.Value()<s.Settled*.15f*(1f-s.Decay))target=Pick(traditional,standard,c);
            if(current==target)return current;
            int ti=Index(target),ci=Index(current);if(ti<0)return current;
            ContactOption option=input.Options[ti];
            float advantage=Clamp((input.Affinities[c,ti]-input.Affinities[c,ci]+100f)/200f);
            float chance=(.003f+.019f*advantage+(s.Governing>=0&&target!=s.Governing?.012f*grievance:0f))*
                (option.Organizers>0?1f+Math.Min(.5f,option.Organizers*.05f):1f);
            if(classResponse)chance+=.012f+.045f*grievance;
            if(person.ClassChanged)chance+=.05f;
            if(s.Revolutionary&&(c==s.Peasant||c==s.Labour)&&option.Has(ContactOption.Left))chance+=.08f;
            chance*=option.Movement;
            if(s.Pressured>=0&&target==s.Pressured)chance*=1f+Math.Min(1f,s.Pressure/50f);
            if(s.Empire&&current==s.Founding&&target!=s.Founding&&
                (s.Pressured>=0&&target==s.Pressured||s.ForeignCity&&target==s.Foreign))chance*=s.External;
            if(age<30)chance*=1.5f*(1f+s.Liberation);else if(age>55)chance*=.6f;
            if(s.Decay>0&&option.Has(ContactOption.Traditional))chance*=1f-s.Decay;
            if(s.Founding>=0&&current==s.Founding&&target!=s.Founding)chance*=1f+s.Fatigue/100f;
            if(s.Trend>=s.GrowthThreshold&&option.Has(ContactOption.Liberal))chance*=1.3f;
            else if(s.Trend<=s.DeclineThreshold&&(option.Has(ContactOption.AgrarianRadical)||option.Has(ContactOption.Authoritarian)))chance*=1.4f;
            float factor=1f;
            if(option.Has(ContactOption.Liberal))factor*=1f+1.5f*s.Prosperity;
            else if(option.Has(ContactOption.Traditional))factor*=1f-.6f*s.Prosperity;
            if(option.Has(ContactOption.Labour))factor*=1f+1.5f*s.Industry;
            if(option.Has(ContactOption.AgrarianTraditional))factor*=1f+s.Settled;
            if(option.Has(ContactOption.AgrarianRadical))factor*=1f+1.5f*s.Landless;
            chance*=factor;
            if(!option.Has(ContactOption.Liberal))chance*=1f+s.Pluralism;
            return random.Value()<chance?target:current;
        }
    }
}
