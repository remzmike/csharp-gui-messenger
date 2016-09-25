using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Deployment.Application;

namespace messages
{
    public class Config
    {
        public static bool SimpleMode()
        {
            return MachineName() == "europa" || MachineName() == "ganymede";
        }

        // todo: make these a radio and load/save with settings
        // mode 1: close hides window
        // mode 2: close minimizes window
        // both: you must exit with tray icon
        public static bool ExitToTrayIcon()
        {
            return !SimpleMode();
        }

        public static bool ExitToTaskBar()
        {
            return !ExitToTrayIcon();
        }

        // todo: make this a checkbox and load/save with settings
        public static bool ShowErrorsFromPop()
        {
            return !(MachineName() == "europa");
        }

        public static bool ShowErrorsFromSubmit()
        {
            return true;
        }

        public static bool ShowErrorsFromLinkClick()
        {
            return true;
        }

        public static string MachineName()
        {
            return Environment.MachineName.ToLower();
        }

        public static string DeployedVersion()
        {
            if (ApplicationDeployment.IsNetworkDeployed)
                return ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
            else
                return "unknown";
        }

    }
}
