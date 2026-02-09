using ai.behaviours;

namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftBehaviourTaskActorLibrary
{
    public static void init()
    {
        var lib = AssetManager.tasks_actor;
        BehaviourTaskActor do_hiring = lib.add(new BehaviourTaskActor
        {
            id = "do_hiring",
            cancellable_by_reproduction = true,
            
        });
    }
}