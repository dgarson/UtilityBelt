﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UtilityBelt.Lib.Models {
    class PersistentVariable {
        public int Id { get; set; }
        public string Server { get; set; }
        public string Character { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
    }
}