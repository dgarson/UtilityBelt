﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UtilityBelt.Lib.Settings {
    internal class OptionResult {
        public ISetting Setting;
        public object Parent;
        public FieldInfo FieldInfo;

        public OptionResult(ISetting obj, FieldInfo fieldInfo, object parent) {
            Setting = obj;
            Parent = parent;
            FieldInfo = fieldInfo;
        }
    }
}
