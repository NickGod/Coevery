﻿using System;
using Orchard.ContentManagement;
using Orchard.ContentManagement.FieldStorage;

namespace Coevery.Fields.Fields {
    public class SelectField : ContentField {

        public int? Value {
            get { return Storage.Get<int?>(Name); }

            set { Storage.Set(value); }
        }
    }
}
