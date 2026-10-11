using System;

namespace EmpireCraft.Scripts.HelperFunc;

public readonly struct ProductionOutput
{
    public readonly float Slots, Rate;
    public readonly bool Food;
    public ProductionOutput(float slots,float rate,bool food){Slots=slots;Rate=rate;Food=food;}
}

public sealed class PopulationProductionInput
{
    public readonly ProductionOutput[] Outputs;
    public readonly float Years, Jobs;
    public readonly bool Modern;
    public readonly string[] Resources;
    public PopulationProductionInput(ProductionOutput[] outputs,float years,float jobs,bool modern,string[] resources=null)
    {Outputs=(ProductionOutput[])outputs.Clone();Years=years;Jobs=jobs;Modern=modern;Resources=resources==null?Array.Empty<string>():(string[])resources.Clone();}
    public PopulationProductionInput Copy()=>new(Outputs,Years,Jobs,Modern,Resources);
    public bool Matches(PopulationProductionInput other)
    {
        if(other==null||Years!=other.Years||Jobs!=other.Jobs||Modern!=other.Modern||Outputs.Length!=other.Outputs.Length)return false;
        if(Resources.Length!=other.Resources.Length)return false;
        for(int i=0;i<Resources.Length;i++)if(Resources[i]!=other.Resources[i])return false;
        for(int i=0;i<Outputs.Length;i++)if(Outputs[i].Slots!=other.Outputs[i].Slots||Outputs[i].Rate!=other.Outputs[i].Rate||Outputs[i].Food!=other.Outputs[i].Food)return false;
        return true;
    }
}

public sealed class PopulationProductionResult
{
    public float[] Outputs;
    public float Subsistence, FoodOutput, FoodNeed, LeatherNeed, Workforce;
}

public static class PopulationProductionMath
{
    public static PopulationProductionResult Compute(PopulationProductionInput input,float households)
    {
        var result=new PopulationProductionResult{Outputs=new float[input.Outputs.Length],
            Subsistence=households*.5f*input.Years,FoodNeed=households*1f*input.Years,
            LeatherNeed=households*.01f*input.Years*(input.Modern?2f:1f),Workforce=households*.6f};
        result.FoodOutput=result.Subsistence;
        for(int i=0;i<result.Outputs.Length;i++)
        {
            ProductionOutput output=input.Outputs[i];
            result.Outputs[i]=output.Slots*output.Rate*input.Years;
            if(output.Food)result.FoodOutput+=result.Outputs[i];
        }
        return result;
    }
}
