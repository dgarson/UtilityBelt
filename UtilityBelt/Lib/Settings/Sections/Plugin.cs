﻿using Decal.Adapter.Wrappers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using UtilityBelt.Constants;
using UtilityBelt.Lib.Constants;

namespace UtilityBelt.Lib.Settings.Sections {
    [Section("Plugin")]
    public class Plugin : SectionBase {
        [Summary("Check for plugin updates on login")]
        [DefaultValue(true)]
        public bool CheckForUpdates {
            get { return (bool)GetSetting("CheckForUpdates"); }
            set { UpdateSetting("CheckForUpdates", value); }
        }

        [Summary("Test")]
        [DefaultValue(ObjectClass.TradeNote)]
        public ObjectClass ObjectClassTest {
            get { return (ObjectClass)GetSetting("ObjectClassTest"); }
            set { UpdateSetting("ObjectClassTest", value); }
        }

        [Summary("BehaviorFlag testing")]
        [DefaultValue(BehaviorFlag.Admin | BehaviorFlag.Corpse)]
        [SupportsFlags]
        public BehaviorFlag BehaviorFlagTest {
            get { return (BehaviorFlag)GetSetting("BehaviorFlagTest"); }
            set { UpdateSetting("BehaviorFlagTest", value); }
        }

        [Summary("Show debug messages")]
        [DefaultValue(false)]
        public bool Debug {
            get { return (bool)GetSetting("Debug"); }
            set { UpdateSetting("Debug", value); }
        }

        [Summary("Main UB Window X position for this character (left is 0)")]
        [DefaultValue(100)]
        public int WindowPositionX {
            get { return (int)GetSetting("WindowPositionX"); }
            set { UpdateSetting("WindowPositionX", value); }
        }

        [Summary("Main UB Window Y position for this character (top is 0)")]
        [DefaultValue(100)]
        public int WindowPositionY {
            get { return (int)GetSetting("WindowPositionY"); }
            set { UpdateSetting("WindowPositionY", value); }
        }

        [Summary("Think to yourself when portal use success/fail")]
        [DefaultValue(false)]
        public bool portalThink {
            get { return (bool)GetSetting("portalThink"); }
            set { UpdateSetting("portalThink", value); }
        }

        [Summary("Timeout to retry portal use")]
        [DefaultValue(5000)]
        public int portalTimeout {
            get { return (int)GetSetting("portalTimeout"); }
            set { UpdateSetting("portalTimeout", value); }
        }
        [Summary("Attempts to retry using a portal")]
        [DefaultValue(3)]
        public int portalAttempts {
            get { return (int)GetSetting("portalAttempts"); }
            set { UpdateSetting("portalAttempts", value); }
        }

        [Summary("Patches the client (in realtime) to disable 3d rendering")]
        [DefaultValue(false)]
        public bool VideoPatch {
            get { return (bool)GetSetting("VideoPatch"); }
            set {
                UpdateSetting("VideoPatch", value);

                if (UBHelper.Core.version < 1911140303) {
                    Util.WriteToChat($"Error UBHelper.dll is out of date!");
                    return;
                }

                if (value) UBHelper.VideoPatch.Enable();
                else UBHelper.VideoPatch.Disable();
            }
        }

        public Plugin(SectionBase parent) : base(parent) {
            Name = "Plugin";
        }
    }
}