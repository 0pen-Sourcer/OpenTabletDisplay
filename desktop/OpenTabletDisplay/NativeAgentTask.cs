using System;
using System.IO;
using System.Security.Principal;

namespace OpenTabletDisplay;

internal static class NativeAgentTask
{
    public static void RegisterAndStart(string executable)
    {
        if (!File.Exists(executable))
            throw new FileNotFoundException("Background connection agent is missing.", executable);

        // Task Scheduler COM registers an interactive, least-privilege logon
        // task without asking users for their Windows password or using
        // elevated PowerShell. An elevated installer creates this task; the
        // agent itself runs at the user's normal integrity level.
        Type com = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new PlatformNotSupportedException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(com)!;
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        dynamic definition = service.NewTask(0);

        string user = WindowsIdentity.GetCurrent().Name;
        definition.RegistrationInfo.Description =
            "Connects your authorized Android device when it is available.";
        definition.Principal.UserId = user;
        definition.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
        definition.Principal.RunLevel = 0;  // TASK_RUNLEVEL_LUA / least privilege

        dynamic trigger = definition.Triggers.Create(9); // TASK_TRIGGER_LOGON
        trigger.Enabled = true;
        trigger.UserId = user;

        dynamic action = definition.Actions.Create(0); // TASK_ACTION_EXEC
        action.Path = executable;
        action.Arguments = "daemon";
        action.WorkingDirectory = AppPaths.Root;

        definition.Settings.Enabled = true;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.MultipleInstances = 2; // IGNORE_NEW

        dynamic task = folder.RegisterTaskDefinition(
            SetupConstants.TaskName, definition, 6, Type.Missing, Type.Missing,
            3, Type.Missing); // CREATE_OR_UPDATE, INTERACTIVE_TOKEN
        task.Run(null);
    }
}
