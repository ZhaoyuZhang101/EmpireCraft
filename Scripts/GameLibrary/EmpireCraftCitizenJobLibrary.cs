namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftCitizenJobLibrary
{
    public static CitizenJobAsset merchant;
    public static void init()
    {
        var cj_lib = AssetManager.citizen_job_library;
        merchant = cj_lib.add(new CitizenJobAsset
        {
            id = "merchant",
            priority = 30,
            debug_option = DebugOption.CitizenJobFarmer,
            path_icon = "ui/Icons/citizen_jobs/iconCitizenJobBuilder"
        });
    }
}