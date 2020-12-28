﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Hellosam.Net.Collections;
using System.Collections.Specialized;
using Newtonsoft.Json;

namespace UBLoader.Lib.Settings {
    public class Setting<T> : ISetting {
        private bool hasDefault = false;
        private T _value;

        public T Value {
            get { return _value; }
            set {
                var validationError = ValidateFunction == null ? null : ValidateFunction(value);
                if (!string.IsNullOrEmpty(validationError)) {
                    FilterCore.LogError($"Unable to set {FullName} to {value}: {validationError}");
                    return;
                }

                var original = _value;
                _value = value;
                if (!hasDefault) {
                    DefaultValue = _value;
                    hasDefault = true;
                }
                if (!_value.Equals(original)) {
                    InvokeChange();
                }
            }
        }

        public T DefaultValue { get; private set; }
        public Func<T, string> ValidateFunction { get; }

        public Setting() {

        }

        public Setting(T initialValue, Func<T, string> validateFunc=null) {
            Value = initialValue;
            ValidateFunction = validateFunc;
            hasDefault = true;

            if (Value != null && Value.GetType().IsGenericType) {
                DefaultValue = (T)Activator.CreateInstance(Value.GetType());
                if (Value is System.Collections.IList valueList) {
                    foreach (var item in valueList)
                        ((System.Collections.IList)DefaultValue).Add(item);
                }
            }
            else {
                DefaultValue = initialValue;
            }

            if (Value != null && Value is INotifyCollectionChanged collection) {
                collection.CollectionChanged += (s, e) => {
                    InvokeChange();
                };
            }
            else if (Value != null && Value is ObservableDictionary<string, string> dict) {
                DefaultValue = (T)Convert.ChangeType(new ObservableDictionary<string, string>(), Value.GetType());
                var defaultDict = DefaultValue as ObservableDictionary<string, string>;
                dict.CollectionChanged += (s, e) => {
                    InvokeChange();
                };
            }
        }

        private static bool IsInstanceOfGenericType(Type genericType, object instance) {
            Type type = instance.GetType();
            while (type != null) {
                if (type.IsGenericType &&
                    type.GetGenericTypeDefinition() == genericType) {
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        public override object GetDefaultValue() {
            return DefaultValue;
        }

        public override object GetValue() {
            return Value;
        }

        public override void SetValue(object newValue) {
            if (Value is ObservableCollection<string>) {
                foreach (var v in (System.Collections.IEnumerable)newValue) {
                    (Value as ObservableCollection<string>).Add(v.ToString());
                }
            }
            else if (Value is ObservableDictionary<string, string>) {
                throw new NotImplementedException();
            }
            else {
                try {
                    Value = (T)Convert.ChangeType(newValue, typeof(T));
                }
                catch {
                    Value = (T)newValue;
                }
            }
        }

        public static implicit operator T(Setting<T> value) {
            return value.Value;
        }

        public override string ToString() {
            return Value.ToString();
        }
    }
}