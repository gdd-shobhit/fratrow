using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ConfigVarAttribute : Attribute
{
    public string Name = null;
    public string DefaultValue = "";
    public ConfigVar.Flags Flags = ConfigVar.Flags.None;
    public string Description = "";
}

public class ConfigVar
{
    public static Dictionary<string, ConfigVar> ConfigVars;
    public static Flags DirtyFlags = Flags.None;

    static bool s_Initialized = false;

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    public static void Init()
    {
        if (s_Initialized)
            return;

        DirtyFlags = Flags.None;
        ConfigVars = new Dictionary<string, ConfigVar>();
        InjectAttributeConfigVars();
        s_Initialized = true;
    }

    public static void ResetAllToDefault()
    {
        foreach (var v in ConfigVars)
        {
            v.Value.ResetToDefault();
        }
    }

    [Serializable]
    public struct FeaturesWrapper
    {
        public FeatureEntry[] features;
    }

    [Serializable]
    public struct FeatureEntry
    {
        public string name;
        public string descripton;
        public string enabled;
        public string value;
    }

    public static void InitializeFeatureToggles(TextAsset featuresJson)
    {
        if (featuresJson != null)
        {
            var features = JsonUtility.FromJson<FeaturesWrapper>(featuresJson.text);
            foreach (var f in features.features)
            {
                if (!ConfigVar.SetFeatureToggle(f.name, f.descripton, (f.enabled == "true"), f.value))
                {
                    Debug.LogWarning(
                        $"Feature '{f.name}' is referenced in '{featuresJson.name}' but does not exist in the project.");
                }
            }
        }

        foreach (var v in ConfigVars)
        {
            if ((v.Value.flags & Flags.FeatureToggle) != 0)
            {
                // surpress initial change check
                v.Value.changed = false;
            }
        }
    }

    public static void GetAllFeaturesToggles(ref List<FeatureToggle> list, bool changedOnly = false)
    {
        list.Clear();
        foreach (var v in ConfigVars)
        {
            if ((v.Value.flags & Flags.FeatureToggle) != 0)
            {
                if (!changedOnly || v.Value.ChangeCheck())
                    list.Add((FeatureToggle)v.Value);
            }
        }
    }

    static bool SetFeatureToggle(string name, string description, bool enabled, string value = "")
    {
        var compareName = $"feature.{name}";
        foreach (var v in ConfigVars)
        {
            if ((v.Value.flags & Flags.FeatureToggle) != 0)
            {
                if (v.Value.name == compareName)
                {
                    var ft = v.Value as FeatureToggle;

                    ft.SetEnabled(enabled);
                    ft.SetValue(value);
                    v.Value.description = description;
                    Debug.Log(
                        $"Feature '{name}' set to {enabled} {(!string.IsNullOrEmpty(value) ? "with value " : "")} {(!string.IsNullOrEmpty(value) ? value : "")}");
                    return true;
                }
            }
        }

        return false;
    }

    public static FeatureToggle FindFeatureToggle(string name)
    {
        return FindFeatureToggle(name.GetHashCode());
    }

    public static FeatureToggle FindFeatureToggle(int nameHash)
    {
        foreach (var v in ConfigVars)
        {
            if ((v.Value.flags & Flags.FeatureToggle) != 0)
            {
                if (v.Value.name.GetHashCode() == nameHash)
                {
                    return (FeatureToggle)v.Value;
                }
            }
        }

        return null;
    }

    public static void SaveChangedVars(string filename)
    {
        if ((DirtyFlags & Flags.Save) == Flags.None)
            return;

        Save(filename);
    }

    public static void Save(string filename)
    {
        using (var st = System.IO.File.CreateText(filename))
        {
            foreach (var cvar in ConfigVars.Values)
            {
                if ((cvar.flags & Flags.Save) == Flags.Save)
                    st.WriteLine("{0} \"{1}\"", cvar.name, cvar.Value);
            }

            DirtyFlags &= ~Flags.Save;
        }

        Debug.Log("saved: " + filename);
    }

    private static readonly Regex validateNameRe = new Regex(@"^[a-z_+-][a-z0-9_+.-]*$");

    public static void RegisterConfigVar(ConfigVar cvar)
    {
        if (ConfigVars.ContainsKey(cvar.name))
        {
            Debug.LogError("Trying to register cvar " + cvar.name + " twice");
            return;
        }

        if (!validateNameRe.IsMatch(cvar.name))
        {
            Debug.LogError("Trying to register cvar with invalid name: " + cvar.name);
            return;
        }

        ConfigVars.Add(cvar.name, cvar);
    }

    [Flags]
    public enum Flags
    {
        None = 0x0, // None
        Save = 0x1, // Causes the cvar to be save to settings.cfg
        Cheat = 0x2, // Consider this a cheat var. Can only be set if cheats enabled
        ServerInfo = 0x4, // These vars are sent to clients when connecting and when changed
        ClientInfo = 0x8, // These vars are sent to server when connecting and when changed
        User = 0x10, // User created variable
        FeatureToggle = 0x20, // Feature toggle
    }

    public ConfigVar(string name, string description, string defaultValue, Flags flags = Flags.None)
    {
        this.name = name;
        this.flags = flags;
        this.description = description;
        this.defaultValue = defaultValue;
    }

    public virtual string Value
    {
        get { return _stringValue; }
        set
        {
            if (_stringValue == value)
                return;
            DirtyFlags |= flags;
            _stringValue = value;
            if (!int.TryParse(value, out _intValue))
                _intValue = 0;
            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _floatValue))
                _floatValue = 0;
            changed = true;
        }
    }

    public int IntValue
    {
        get { return _intValue; }
        set
        {
            _stringValue = value.ToString();
            _intValue = value;
            _floatValue = value;
            changed = true;
        }
    }

    public float FloatValue
    {
        get { return _floatValue; }
    }

    static void InjectAttributeConfigVars()
    {
        var classes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName.StartsWith("Assembly-CSharp"))
            .SelectMany(a => a.GetTypes());

        foreach (var _class in classes)
        {
            if (!_class.IsClass)
                continue;
            foreach (var field in _class.GetFields(System.Reflection.BindingFlags.Instance |
                                                   System.Reflection.BindingFlags.Static |
                                                   System.Reflection.BindingFlags.NonPublic |
                                                   System.Reflection.BindingFlags.Public))
            {
                if (!field.IsDefined(typeof(ConfigVarAttribute), false) &&
                    !field.IsDefined(typeof(FeatureToggleAttribute), false))
                    continue;

                if (!field.IsStatic)
                {
                    Debug.LogError("Cannot use ConfigVar attribute on non-static fields");
                    continue;
                }

                if (field.FieldType != typeof(ConfigVar) && field.FieldType != typeof(FeatureToggle))
                {
                    Debug.LogError(
                        $"Cannot use ConfigVar attribute on fields not of type ConfigVar or FeatureToggle - {field.Name} type {field.FieldType}");
                    continue;
                }

                var configAttrs = field.GetCustomAttributes(typeof(ConfigVarAttribute), false);
                if (configAttrs.Length > 0)
                {
                    var configAttr = configAttrs[0] as ConfigVarAttribute;
                    if (configAttr != null)
                    {
                        var name = configAttr.Name != null
                            ? configAttr.Name
                            : _class.Name.ToLower() + "." + field.Name.ToLower();
                        var cvar = field.GetValue(null) as ConfigVar;
                        if (cvar != null)
                        {
                            Debug.LogError("ConfigVars (" + name +
                                           ") should not be initialized from code; just marked with attribute");
                            continue;
                        }

                        cvar = new ConfigVar(name, configAttr.Description, configAttr.DefaultValue, configAttr.Flags);
                        cvar.ResetToDefault();
                        RegisterConfigVar(cvar);
                        field.SetValue(null, cvar);
                    }
                }

                // is it a feature toggle?
                var featureAttrs = field.GetCustomAttributes(typeof(FeatureToggleAttribute), false);
                if (featureAttrs.Length > 0)
                {
                    var featureAttr = featureAttrs[0] as FeatureToggleAttribute;
                    if (featureAttr != null)
                    {
                        var name = featureAttr.Name != null
                            ? featureAttr.Name
                            : _class.Name.ToLower() + "." + field.Name.ToLower();
                        var cvar = field.GetValue(null) as FeatureToggle;
                        if (cvar != null)
                        {
                            Debug.LogError("FeatureToggles (" + name +
                                           ") should not be initialized from code; just marked with attribute");
                            continue;
                        }

                        // does it exist already?
                        var featureName = $"feature.{name}";
                        var existingFeature = FindFeatureToggle(featureName);
                        if (existingFeature != null)
                        {
                            existingFeature.SetEnabled(featureAttr.Default);
                            field.SetValue(null, existingFeature);
                        }
                        else
                        {
                            cvar = new FeatureToggle(featureName, "0", Flags.FeatureToggle);
                            cvar.SetEnabled(featureAttr.Default);
                            RegisterConfigVar(cvar);
                            field.SetValue(null, cvar);
                        }
                    }
                }
            }
        }

        // Clear dirty flags as default values shouldn't count as dirtying
        DirtyFlags = Flags.None;
    }

    void ResetToDefault()
    {
        this.Value = defaultValue;
    }

    public bool ChangeCheck()
    {
        if (!changed)
            return false;
        changed = false;
        return true;
    }

    public override string ToString()
    {
        return string.Format("{0} = {1}", name, Value);
    }

    public readonly string name;
    public string description;
    public readonly string defaultValue;
    public readonly Flags flags;
    public bool changed;

    protected string _stringValue;
    protected float _floatValue;
    protected int _intValue;
}

public class FeatureToggleAttribute : Attribute
{
    public string Name = null;
    public bool Default = false;
}

public class FeatureToggle : ConfigVar
{
    public event Action<bool> OnToggle;

    public FeatureToggle(string name, string value, Flags flags = Flags.None) : base(name, value, "", flags)
    {
    }

    public bool IsEnabled => (IntValue != 0);

    public void SetEnabled(bool enabled)
    {
        bool wasEnabled = _intValue != 0;

        if (enabled != wasEnabled)
        {
            OnToggle?.Invoke(enabled);
            _intValue = (enabled ? 1 : 0);
            changed = true;
        }
    }

    public void SetValue(string value)
    {
        _stringValue = value;

        // for a feature toggle we also want to try and set the float value too
        if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _floatValue))
            _floatValue = 0f;

        changed = true;
    }

    public override string ToString()
    {
        return string.Format("{0} = {1} {2}", name, IsEnabled, !string.IsNullOrEmpty(Value) ? $"({Value})" : "");
    }

    public static bool IsAllowedByFeature(string feature)
    {
        if (!string.IsNullOrEmpty(feature))
        {
            bool invert = false;
            if (feature.StartsWith("!"))
            {
                invert = true;
                feature = feature.Substring(1);
            }

            // look up feature, is it enabled?
            var featureName = $"feature.{feature}";
            var toggle = ConfigVar.FindFeatureToggle(featureName);
            if (toggle != null)
            {
                return invert ? !toggle.IsEnabled : toggle.IsEnabled;
            }
            else
            {
                Debug.LogWarning($"IsAllowedByFeature looking for feature {feature} but it doesn't seem to exist");
                return invert ? false : true;
            }
        }

        return true;
    }
}


/*
// Slower variant of ConfigVar that is backed by code. Useful for wrapping Unity API's
// into ConfigVars but beware that performance is not the same as a normal ConfigVar.

public class ConfigVarVirtual : ConfigVar
{
    public delegate void SetValue(string val);
    public delegate string GetValue();
    public ConfigVarVirtual(string name, string value, string description, GetValue getter, SetValue setter, Flags flags = Flags.None) : base(name, description, flags)
    {
        m_Getter = getter;
        m_Setter = setter;
        Value = value;
    }

    public override string Value
    {
        get { return m_Getter();  }
        set { m_Setter(value); }
    }

    // These methods are made 'new' to avoid the base class having to
    // make IntValue and FloatValue virtual
    public new int IntValue
    {
        get { int res; int.TryParse(Value, out res); return res; }
    }

    public new float FloatValue
    {
        get { float res; float.TryParse(Value, out res); return res; }
    }

    SetValue m_Setter;
    GetValue m_Getter;
}

*/