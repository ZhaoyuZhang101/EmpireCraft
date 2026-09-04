$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$helper = Get-Content (Join-Path $root 'Scripts/HelperFunc/EmpirePopulation.cs') -Raw
$empire = Get-Content (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$names = @('getUnits', 'GetMembersWithTrait', 'countTotalMoney', 'countHappyUnits', 'countSick', 'countHungry',
    'countStarving', 'countChildren', 'countAdults', 'countHomeless', 'countMales', 'countFemales', 'countHoused')
$methods = foreach ($name in $names) {
    $match = [regex]::Match($empire, '(?ms)^    public (?:override )?[^\r\n]+\b' + $name + '\([^\r\n]*\)\s*\{.*?^    \}')
    if (-not $match.Success) { throw "Missing actual method: $name" }
    $match.Value
}
$stubs = @'
using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.HelperFunc;
public class Data { }
public class ActorAsset { public bool is_boat; }
public class Actor {
    public Data data = new Data(); public ActorAsset asset = new ActorAsset();
    public Kingdom kingdom; public bool alive = true, aw, adult, baby, housed, male, female, happy, hungry, starving, sick;
    public int money; public string trait;
    public bool isAlive() { return alive; }
    public bool hasTrait(string value) { return trait == value; }
}
public class Kingdom {
    public Data data = new Data(); public bool dead, aw;
    public List<Actor> units = new List<Actor>();
    public bool isRekt() { return dead; }
    public IEnumerable<Actor> getUnits() {
        foreach (var a in units) if (a.isAlive() && !a.asset.is_boat && a.kingdom == this) yield return a;
    }
    public int countAdults() { return getUnits().Count(a => a.adult); }
}
namespace EmpireCraft.Scripts.Compatibility {
    public static class AncientWarfareCompatibility {
        public static bool Loaded;
        public static bool Owns(Kingdom k) { return Loaded && k != null && k.aw; }
        public static bool Owns(Actor a) { return Loaded && a != null && (a.aw || Owns(a.kingdom)); }
    }
}
public class EmpireData { }
public class MetaObject<T> {
    public virtual IEnumerable<Actor> getUnits() { throw new Exception("Must use Empire's safe iterator"); }
    public virtual int countTotalMoney() { return getUnits().Sum(a => a.money); }
    public virtual int countHappyUnits() { return getUnits().Count(a => a.happy); }
    public virtual int countSick() { return getUnits().Count(a => a.sick); }
    public virtual int countHungry() { return getUnits().Count(a => a.hungry); }
    public virtual int countStarving() { return getUnits().Count(a => a.starving); }
    public virtual int countChildren() { return getUnits().Count(a => a.baby); }
    public virtual int countAdults() { return getUnits().Count(a => a.adult); }
    public virtual int countHomeless() { return getUnits().Count(a => !a.housed); }
    public virtual int countMales() { return getUnits().Count(a => a.male); }
    public virtual int countFemales() { return getUnits().Count(a => a.female); }
    public virtual int countHoused() { return getUnits().Count(a => a.housed); }
}
public static class PopulationTests {
    private static int checks;
    private static void Check(bool pass, string reason) { if (!pass) throw new Exception(reason); checks++; }
    public static int Run() {
        var k = new Kingdom();
        var other = new Kingdom();
        var adult = new Actor { kingdom = k, adult = true, male = true, happy = true, housed = true, money = 10, trait = "royal" };
        var child = new Actor { kingdom = k, baby = true, female = true, sick = true, hungry = true, starving = true, money = 2, trait = "royal" };
        var foreign = new Actor { kingdom = other, adult = true };
        var xiaActor = new Actor { kingdom = k, aw = true, adult = true };
        k.units.AddRange(new[] { null, adult, new Actor { kingdom = k, data = null },
            new Actor { kingdom = k, asset = null }, new Actor { kingdom = k, alive = false },
            new Actor { kingdom = k, asset = new ActorAsset { is_boat = true } }, foreign, child, xiaActor });
        bool reproduced = false;
        try { k.countAdults(); } catch (NullReferenceException) { reproduced = true; }
        Check(reproduced, "Reproduce logged vanilla getUnits null-slot crash");
        AncientWarfareCompatibility.Loaded = true;
        var aw = new Kingdom { aw = true };
        aw.units.Add(new Actor { kingdom = aw, adult = true });
        var invalid = new Kingdom { data = null };
        var e = new Empire { kingdoms_list = new List<Kingdom> { null, invalid, new Kingdom { dead = true }, new Kingdom { units = null }, k, k, aw } };
        e.kingdoms_hashset = new HashSet<Kingdom>(e.kingdoms_list);
        Check(e.getUnits().SequenceEqual(new[] { adult, child }), "Skip null/stale/dead/boat/transferred/AW records; retain live people");
        Check(e.GetMembersWithTrait("royal").SequenceEqual(new[] { adult, child }), "Trait/peerage scanning uses safe population");
        Check(e.countAdults() == 1, "Adults"); Check(e.countChildren() == 1, "Children");
        Check(e.countTotalMoney() == 12, "Money"); Check(e.countHappyUnits() == 1, "Happy");
        Check(e.countSick() == 1, "Sick"); Check(e.countHungry() == 1, "Hungry");
        Check(e.countStarving() == 1, "Starving"); Check(e.countHomeless() == 1, "Homeless");
        Check(e.countHoused() == 1, "Housed"); Check(e.countMales() == 1, "Male"); Check(e.countFemales() == 1, "Female");
        Check(k.units.Count == 9 && k.units[0] == null && k.units.Contains(foreign), "Do not rewrite source membership index");
        var iterator = e.getUnits().GetEnumerator();
        Check(iterator.MoveNext() && iterator.Current == adult, "Start snapshot");
        k.units.Clear(); e.kingdoms_list.Clear();
        Check(iterator.MoveNext() && iterator.Current == child, "Source removal during yield does not invalidate enumeration");
        Check(!iterator.MoveNext(), "Snapshot finishes without duplicates"); iterator.Dispose();
        k.units.AddRange(new[] { adult, child });
        e.kingdoms_list.Add(k);
        iterator = e.getUnits().GetEnumerator(); iterator.MoveNext(); child.kingdom = other;
        Check(!iterator.MoveNext(), "Revalidate membership after relocation"); iterator.Dispose(); child.kingdom = k;
        iterator = e.getUnits().GetEnumerator(); iterator.MoveNext(); child.data = null;
        Check(!iterator.MoveNext(), "Revalidate actor disposal after yield"); iterator.Dispose(); child.data = new Data();
        iterator = e.getUnits().GetEnumerator(); iterator.MoveNext(); k.dead = true;
        Check(!iterator.MoveNext(), "Stop scanning disposed kingdom"); iterator.Dispose(); k.dead = false;
        k.units.Add(xiaActor); e.kingdoms_list.Add(aw);
        AncientWarfareCompatibility.Loaded = false;
        Check(e.countAdults() == 3, "Standalone mod does not exclude Xia-marked actors/kingdoms");
        e.kingdoms_list = null;
        Check(e.countAdults() == 0 && !e.getUnits().Any(), "Uninitialized empire population safe");
        return checks;
    }
}
'@
$model = "`npublic class Empire : MetaObject<EmpireData> { public List<Kingdom> kingdoms_list; public HashSet<Kingdom> kingdoms_hashset; $($methods -join "`n") }`n"
Add-Type -TypeDefinition ($stubs + $model + [regex]::Replace($helper, '(?m)^using [^;]+;\s*', ''))
Write-Output "$([PopulationTests]::Run()) population/crash regression assertions passed."
