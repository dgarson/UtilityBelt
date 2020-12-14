﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace UtilityBelt.Lib.Settings {
    public class PluginMessageDisplay : ISetting {
        [Summary("Enabled / Disabled")]
        public readonly Setting<bool> Enabled = new Setting<bool>();

        [Summary("Color")]
        public readonly Setting<short> Color = new Setting<short>();

        public PluginMessageDisplay(bool show, short color) : base() {
            Enabled.Value = show;
            Color.Value = color;
        }

        new public string ToString() {
            return $"Enabled:{Enabled} Color:{Color}";
        }
    }
}
