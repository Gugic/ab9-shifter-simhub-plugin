using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AB9ActiveShifter.Core;
using GameReaderCommon;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimHub.Plugins;
using SimHub.Plugins.DataPlugins.ShakeItV3.Device;
using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
using SimHub.Plugins.DataPlugins.ShakeItV3.Filters;
using SimHub.Plugins.DataPlugins.ShakeItV3.Outputs.Audio;
using SimHub.Plugins.DataPlugins.ShakeItV3.Settings;
using SimHub.Plugins.DataPlugins.ShakeItV3.UI;

namespace AB9ActiveShifter.Effects
{
    /// <summary>
    /// Hosts SimHub's effect engine and editor, while the hardware remains owned by our loop.
    /// Only copied tones cross that boundary. Native profile construction and persistence stay
    /// on the dispatcher; the data callback skips a busy profile swap rather than waiting for UI.
    /// </summary>
    public sealed class NativeEffectsService
    {
        private readonly object _sync = new object();
        private readonly NativeEffectsHost _host;
        private readonly DispatcherTimer _saveTimer;
        private AB9ShifterPlugin _plugin;
        private ShifterSettings _owner;
        private ShakeItProfile _profile;
        private int _epoch;
        private string _lastJson;
        private bool _saving;

        public string Error { get; private set; }
        public bool Ready { get { return Error == null && _profile != null; } }
        public NativeEffectsSettings Settings { get { return (NativeEffectsSettings)_host.Settings; } }
        public object ProfileIdentity { get { return _profile; } }

        public NativeEffectsService(AB9ShifterPlugin plugin, PluginManager manager)
        {
            _plugin = plugin;
            _host = new NativeEffectsHost { PluginManager = manager };
            _host.Init(manager, null);
            _host.Settings.Init();
            Settings.Service = this;
            Settings.GlobalGain = 100;
            Settings.Profiles.Clear();
            _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(250) };
            _saveTimer.Tick += delegate { SaveIfChanged(true); };
            _saveTimer.Start();
        }

        public void Bind(AB9ShifterPlugin plugin)
        {
            _plugin = plugin;
            SelectProfile(plugin.Settings);
        }

        public void SelectProfile(ShifterSettings owner)
        {
            if (ReferenceEquals(owner, _owner) && owner.NativeEffectsJson != null) return;
            lock (_sync)
            {
                SaveIfChanged(false);
                Publish(NativeEffectFrame.Silent);
                _epoch = unchecked(_epoch + 1);
                _owner = owner;
                Settings.Owner = owner;
                Error = null;
                try
                {
                    JObject saved = string.IsNullOrEmpty(owner.NativeEffectsJson)
                        ? null : JObject.Parse(NativeEffectsData.Validate(owner.NativeEffectsJson));
                    _profile = saved == null ? BuildLegacyProfile(owner) : saved["Profile"].ToObject<ShakeItProfile>();
                    _profile.Name = "Lever effects";
                    _profile.IncludeOutputSettingsInProfile = false;
                    Settings.GlobalGain = saved == null ? 100 : (double?)saved["GlobalGain"] ?? 100;
                    Settings.IsMuted = saved != null && ((bool?)saved["IsMuted"] ?? false);
                    Settings.Profiles.Clear();
                    Settings.Profiles.Add(_profile);
                    Settings.InitProfile(_profile);
                    Settings.CurrentProfile = _profile;
                    Settings.SetGameCapabilities(Settings.GameCapabilities, Settings.CurrentGame);
                    foreach (EffectsContainerBase container in _profile.GetAllContainers(true))
                    {
                        if (container.Filter != null) container.Filter.SetDefaults();
                        // Materialise the one channel before taking the migration baseline.
                        // Opening its native mapping control then cannot look like an edit.
                        if (!container.IsGroup)
                            new SimHub.Plugins.DataPlugins.ShakeItV3.Device.MotorsWithFrequency.MotorsWithFrequencyOutputManagerEffectsChannelsModel(container, Settings.OutputManager);
                    }
                    _lastJson = Serialize();
                    owner.SetNativeEffectsSilently(_lastJson);
                    Log.Info("Native effects ready (" + _profile.GetAllContainers(true).Count() + " rows, profile epoch " + _epoch + ").");
                }
                catch (Exception ex)
                {
                    _profile = null;
                    Error = "SimHub's effects editor could not load this profile: " + ex.Message;
                    Log.Error("Native effects profile failed to load", ex);
                }
            }
        }

        public FrameworkElement CreateEditor()
        {
            if (!Ready) return new TextBlock { Text = Error ?? "The native effects editor is loading.", TextWrapping = TextWrapping.Wrap };
            try
            {
                // Starts SimHub's own live-preview timer. The full settings control is retained by
                // its host; only its profile editor is presented in our existing Effects tab.
                _host.GetWPFSettingsControl(_host.PluginManager);
                // EffectsListMain is the native editor's add/group/calibrate toolbar and effect list.
                // Its DataContext is one profile: our existing profile picker remains the authority.
                return new EffectsListMain { Settings = Settings, DataContext = _profile };
            }
            catch (Exception ex)
            {
                Log.Error("Native effects editor failed to load", ex);
                return new TextBlock { Text = "SimHub's effects editor could not open: " + ex.Message, TextWrapping = TextWrapping.Wrap };
            }
        }

        public void Configure(EngineConfig cfg)
        {
            cfg.NativeEffectsEnabled = true;
            cfg.NativeEffectsEpoch = _epoch;
            if (!Ready || !ReferenceEquals(_owner, _plugin.Settings)) { cfg.GrindEnabled = false; return; }
            cfg.GrindEnabled = _profile.GetAllContainers(true)
                .OfType<AB9GrindEffectContainer>().Any(c => c.GetEffectiveIsEnabled());
        }

        public void DataUpdate(PluginManager manager, ref GameData data, double custom)
        {
            if (!Ready || data == null) { Publish(NativeEffectFrame.Silent); return; }
            if (!Monitor.TryEnter(_sync)) { Publish(NativeEffectFrame.Silent); return; }
            try
            {
                Settings.SampledCustomValue = custom;
                _host.DataUpdate(manager, ref data);
            }
            catch (Exception ex)
            {
                Publish(NativeEffectFrame.Silent);
                Log.ErrorThrottled("native-effects", "Native effect update failed", ex);
            }
            finally { Monitor.Exit(_sync); }
        }

        public void Publish(NativeEffectFrame frame)
        {
            ShifterEngine engine = AB9ShifterPlugin.Engine;
            if (engine != null) engine.SetNativeEffects(frame);
        }

        public int Epoch { get { return _epoch; } }

        public void SaveIfChanged(bool notify)
        {
            if (_saving || !Ready || _owner == null) return;
            lock (_sync)
            {
                _saving = true;
                try
                {
                    string json = Serialize();
                    if (json == _lastJson) return;
                    var before = JObject.Parse(_lastJson);
                    var after = JObject.Parse(json);
                    if (JToken.DeepEquals(before, after)) { _lastJson = json; return; }
                    _lastJson = json;
                    if (notify) _owner.NativeEffectsJson = json;
                    else _owner.SetNativeEffectsSilently(json);
                }
                catch (Exception ex) { Log.ErrorThrottled("native-effects-save", "Could not save native effects", ex); }
                finally { _saving = false; }
            }
        }

        private string Serialize()
        {
            JObject root = new JObject
            {
                ["Version"] = 1,
                ["GlobalGain"] = Settings.GlobalGain,
                ["IsMuted"] = Settings.IsMuted,
                ["Profile"] = JObject.FromObject(_profile)
            };
            // Native editor expansion and live preview state are not tuning changes. Excluding
            // them also prevents merely opening a preset's effect from forking that preset.
            foreach (JObject obj in root.DescendantsAndSelf().OfType<JObject>())
            {
                obj.Remove("EditorExpanded");
                obj.Remove("IsSelected");
                obj.Remove("OutputManager");
                obj.Remove("IncludeOutputSettingsInProfile");
            }
            return JsonConvert.SerializeObject(root, Formatting.None);
        }

        public void Reset()
        {
            lock (_sync)
            {
                // The settings reset clears the migration marker; don't save the old native
                // tree back over it before rebuilding all rows from the reset dials.
                _lastJson = null;
                _profile = null;
                SelectProfile(_plugin.Settings);
                _owner.NativeEffectsJson = _lastJson;
            }
        }

        public void Stop()
        {
            _saveTimer.Stop();
            Publish(NativeEffectFrame.Silent);
            SaveNativeState();
            _host.FinalizePlugin();
        }

        public void SaveNativeState()
        {
            if (!Ready) return;
            lock (_sync)
            {
                // This public host method is also the native calibration persistence hook.
                // Its auxiliary settings file is ignored by our fromDevice host on reload;
                // named shifter profiles remain authoritative for every effect tune.
                try { _host.SaveSettings(); }
                catch (Exception ex) { Log.Error("Could not save native effect calibration", ex); }
            }
        }

        private ShakeItProfile BuildLegacyProfile(ShifterSettings s)
        {
            var profile = new ShakeItProfile { Name = "Lever effects", Settings = Settings };
            Add(profile, new AB9GrindEffectContainer(), s.GrindEnabled, s.GrindGainPct, s.GrindFreqHz);
            var bite = Add(profile, new AB9BiteEffectContainer(), s.FxBiteEnabled, s.FxBiteGainPct, s.FxBiteFreqHz);
            ((PulseFilter)bite.Filter).Duration = s.FxBiteDurationMs;

            var rpm = Add(profile, new RPMContainer(), s.FxEngineEnabled, s.FxEngineGainPct, s.FxEngineFreqAt1000Rpm);
            var rpmOutput = (ToneOutput)rpm.Output;
            ((SplineFilter)rpm.Filter).ControlPoints = new ObservableCollection<ControlPoint>
            { new ControlPoint(0, 0), new ControlPoint(1, 100), new ControlPoint(100, 100) };
            rpmOutput.UseHighFrequency = true;
            rpmOutput.HighFrequency = Math.Min(NativeEffectMixer.MaximumFrequency, s.FxEngineFreqAt1000Rpm * 7);
            rpmOutput.UseWhiteNoise = false;
            Add(profile, new AB9LimiterEffectContainer(), s.FxLimiterEnabled, s.FxLimiterGainPct, s.FxLimiterFreqHz);
            var abs = Add(profile, new ABSActiveEffectContainer(), s.FxAbsEnabled, s.FxAbsGainPct, s.FxAbsFreqHz);
            ((PulseFilter)abs.Filter).Duration = 0;
            var tc = Add(profile, new TCActiveEffectContainer(), s.FxTcEnabled, s.FxTcGainPct, s.FxTcFreqHz);
            ((PulseFilter)tc.Filter).Duration = 0;
            Add(profile, new WheelsImpactContainer(), s.FxCurbsEnabled, s.FxCurbsGainPct, s.FxCurbsFreqHz);
            var shift = Add(profile, new GearEffectContainer(), s.FxShiftEnabled, s.FxShiftGainPct, s.FxShiftFreqHz);
            ((PulseFilter)shift.Filter).Duration = s.FxShiftDurationMs;
            Add(profile, new AB9PropertyEffectContainer(), s.FxCustomEnabled, s.FxCustomGainPct, s.FxCustomFreqHz);
            return profile;
        }

        private static T Add<T>(ShakeItProfile profile, T container, bool enabled, int gain, int frequency)
            where T : EffectsContainerBase
        {
            container.SetDefaults(OutputMode.MotorsWithFrequency);
            container.IsEnabled = enabled;
            container.Gain = gain;
            container.EditorExpanded = false;
            var output = container.Output as SingleToneOutput;
            if (output != null) output.Frequency = frequency;
            profile.EffectsContainers.Add(container);
            return container;
        }
    }

    public sealed class NativeEffectsSettings : ShakeItDeviceSettings<AB9EffectOutputManager>
    {
        [JsonIgnore] public ShifterSettings Owner { get; set; }
        [JsonIgnore] public NativeEffectsService Service { get; set; }
        [JsonIgnore] public double SampledCustomValue { get; set; }
    }

    // ISubPlugin inherited from the native host prevents this helper being discovered as
    // a second standalone plugin. It owns no DirectInput handle and saves no separate tune.
    public sealed class NativeEffectsHost : ShakeITV3PluginDevice<AB9EffectOutputManager, NativeEffectsSettings>
    {
        public NativeEffectsHost() : base("AB9ActiveShifterEffects", "Lever", true) { }
    }
}
