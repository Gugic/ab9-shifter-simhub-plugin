// Reference-only native ShakeIt surface. Declarations were checked against the installed
// assembly; bodies never run. Verify-StubBuild.ps1 must bind this build to real SimHub.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using GameReaderCommon;
using GameReaderCommon.Enums;
using GameReaderCommon.Feedback;
using Newtonsoft.Json.Linq;

namespace SimHub.Plugins.ProfilesCommon
{
    public abstract class ProfileBase<TProfile, TSettings>
    {
        public string Name { get; set; }
        public TSettings Settings { get; set; }
    }
    public abstract class ProfileSettingsBase<TProfile, TSettings>
    {
        public virtual void Init() { }
        public ObservableCollection<TProfile> Profiles { get; } = new ObservableCollection<TProfile>();
        public TProfile CurrentProfile { get; set; }
        public string CurrentGame { get; }
        public virtual void InitProfile(TProfile profile) { }
    }
}

namespace SimHub.Plugins.Devices
{
    public class DeviceSettingControl { }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Calibration
{
    public class CalibratedFeedbackData { }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Utilities
{
    public abstract class EditableBase
    {
        protected abstract UserControl GetEditControl();
    }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Effects;
    using SimHub.Plugins.DataPlugins.ShakeItV3.Filters;
    using SimHub.Plugins.DataPlugins.ShakeItV3.Outputs;
    using SimHub.Plugins.DataPlugins.ShakeItV3.Outputs.Audio;
    using SimHub.Plugins.DataPlugins.ShakeItV3.Settings;
    using SimHub.Plugins.DataPlugins.ShakeItV3.Utilities;

    public enum OutputMode { Motors, Shakers, Any, MotorsWithFrequency }
    public enum DeviceTarget { None, Generic, Interhaptics }
    public class FrequencyRange
    {
        public FrequencyRange(int minimum, int maximum, bool hasFrequency = true) { }
    }
    public interface IContainerGroup { }
    public static class IContainerGroupExtensions
    {
        public static IEnumerable<EffectsContainerBase> GetAllContainers(this IContainerGroup group, bool includeGroups = false)
        { return null; }
    }
    public abstract class EffectsContainerBase : EditableBase
    {
        protected abstract IEnumerable<EffectBase> AvailableEffects { get; }
        public abstract FilterBase Filter { get; }
        public AbstractSettingsStore SettingsStore { get; } = new AbstractSettingsStore();
        public bool IsEnabled { get; set; }
        public double Gain { get; set; }
        public bool EditorExpanded { get; set; }
        public OutputBase Output { get; set; }
        public ShakeItProfile ParentProfile { get; set; }
        public virtual bool IsGroup { get; }
        public virtual double GetEffectiveGain() { return 0; }
        public bool GetEffectiveIsEnabled() { return false; }
        public virtual double GetTestDuration() { return 0; }
        public abstract void SetDefaults(OutputMode mode);
    }
    public abstract class EffectsContainerBase<TOutput> : EffectsContainerBase where TOutput : AudioOutputBase
    {
        public override void SetDefaults(OutputMode mode) { }
        public abstract void SetAudioDefaults(TOutput output);
    }
    public class RPMContainer : EffectsContainerBase<ToneOutput>
    {
        protected override IEnumerable<EffectBase> AvailableEffects { get { return null; } }
        public override FilterBase Filter { get { return null; } }
        protected override UserControl GetEditControl() { return null; }
        public override void SetAudioDefaults(ToneOutput output) { }
    }
    public class ABSActiveEffectContainer : EffectsContainerBase<SingleToneOutput>
    {
        protected override IEnumerable<EffectBase> AvailableEffects { get { return null; } }
        public override FilterBase Filter { get { return null; } }
        protected override UserControl GetEditControl() { return null; }
        public override void SetAudioDefaults(SingleToneOutput output) { }
    }
    public class TCActiveEffectContainer : EffectsContainerBase<SingleToneOutput>
    {
        protected override IEnumerable<EffectBase> AvailableEffects { get { return null; } }
        public override FilterBase Filter { get { return null; } }
        protected override UserControl GetEditControl() { return null; }
        public override void SetAudioDefaults(SingleToneOutput output) { }
    }
    public class WheelsImpactContainer : EffectsContainerBase<ToneOutput>
    {
        protected override IEnumerable<EffectBase> AvailableEffects { get { return null; } }
        public override FilterBase Filter { get { return null; } }
        protected override UserControl GetEditControl() { return null; }
        public override void SetAudioDefaults(ToneOutput output) { }
    }
    public class GearEffectContainer : EffectsContainerBase<SingleToneOutput>
    {
        protected override IEnumerable<EffectBase> AvailableEffects { get { return null; } }
        public override FilterBase Filter { get { return null; } }
        protected override UserControl GetEditControl() { return null; }
        public override void SetAudioDefaults(SingleToneOutput output) { }
    }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers.Attributes
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
    public class ShakeItContainerMetadataAttribute : Attribute
    {
        public ShakeItContainerMetadataAttribute(int effectOrder, string name, string description, string group,
            OutputMode effectPlatform = OutputMode.Any, bool hidden = false, DeviceTarget includeTarget = DeviceTarget.Generic,
            DeviceTarget exceptTarget = DeviceTarget.None)
        { }
    }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Effects
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Calibration;
    using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
    public abstract class EffectBase
    {
        public FFBPlacement Placement { get; }
        public bool TestMode { get; set; }
        public DateTime? TestStart { get; set; }
        public EffectBase(string name, FFBPlacement location) { }
        public virtual void Idle() { }
        public virtual void ResetData() { }
        public abstract bool GetIsSupportedOnCurrentGame(FeedbackCapabilities caps);
        public abstract double GetEffectValue(GameData data, CalibratedFeedbackData calibratedFeedback, EffectsContainerBase effectsContainer);
    }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Outputs
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Utilities;
    public class EffectOutput { }
    public abstract class OutputBase : EditableBase { }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Outputs.Audio
{
    public abstract class AudioOutputBase : OutputBase { }
    public class SingleToneOutput : AudioOutputBase
    {
        public int Frequency { get; set; }
        protected override UserControl GetEditControl() { return null; }
    }
    public class ToneOutput : SingleToneOutput
    {
        public bool UseHighFrequency { get; set; }
        public int HighFrequency { get; set; }
        public bool UseWhiteNoise { get; set; }
    }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Outputs.Audio.Renderers
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Effects;
    using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
    public interface IAudioRenderer
    {
        bool IsPreemptive { get; }
        EffectBase Effect { get; }
        EffectsContainerBase Container { get; }
        void Update(DateTime refTime);
        float GetCurrentGain();
    }
    public interface IToneRenderer : IAudioRenderer
    {
        float GetCurrentFrequency();
        int GetCurrentDelay();
    }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Filters
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Utilities;
    public abstract class FilterBase : EditableBase
    { public virtual void SetDefaults() { } }
    public class SimpleGammaFilter : FilterBase
    {
        protected override UserControl GetEditControl() { return null; }
    }
    public class SplineFilter : FilterBase
    {
        public ObservableCollection<ControlPoint> ControlPoints { get; set; }
        protected override UserControl GetEditControl() { return null; }
    }
    public class ControlPoint
    {
        public ControlPoint(double X, double Y) { }
    }
    public class PulseFilter : FilterBase
    {
        public int Duration { get; set; }
        protected override UserControl GetEditControl() { return null; }
    }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Settings
{
    using SimHub.Plugins.ProfilesCommon;
    using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
    public interface IOutputManager { }
    public interface IDeviceOutputManager : IOutputManager { }
    public abstract class AbstractSettingsBase { }
    public class AbstractSettingsStore
    {
        public T GetSettings<T>() where T : AbstractSettingsBase, new() { return null; }
    }
    public class ShakeItProfile : ProfileBase<ShakeItProfile, ShakeItSettings>, IContainerGroup
    {
        public bool IncludeOutputSettingsInProfile { get; set; }
        public ObservableCollection<EffectsContainerBase> EffectsContainers { get; } = new ObservableCollection<EffectsContainerBase>();
    }
    public abstract class ShakeItSettings : ProfileSettingsBase<ShakeItProfile, ShakeItSettings>
    {
        public double GlobalGain { get; set; }
        public bool IsMuted { get; set; }
        public FeedbackCapabilities GameCapabilities { get; set; }
        public override void InitProfile(ShakeItProfile profile) { }
        public void SetGameCapabilities(FeedbackCapabilities capabilities, string currentGame) { }
    }
    public class ShakeItSettings<T> : ShakeItSettings where T : IOutputManager
    { public T OutputManager { get; set; } }
    public class ShakeItDeviceSettings<T> : ShakeItSettings<T> where T : IOutputManager { }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Settings;
    public abstract class ShakeITV3PluginBase<T, TSettings> where T : IOutputManager where TSettings : ShakeItSettings<T>, new()
    {
        public PluginManager PluginManager { get; set; }
        public ShakeItSettings Settings { get; }
        public ShakeITV3PluginBase(string prefix, string title, bool fromDevice = false) { }
        public void Init(PluginManager manager, JToken token) { }
        public void DataUpdate(PluginManager manager, ref GameData data) { }
        public Control GetWPFSettingsControl(PluginManager manager) { return null; }
        public void FinalizePlugin() { }
        public void SaveSettings() { }
    }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Device
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Device.MotorsWithFrequency;
    using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
    using SimHub.Plugins.DataPlugins.ShakeItV3.Outputs;
    using SimHub.Plugins.DataPlugins.ShakeItV3.Outputs.Audio.Renderers;
    using SimHub.Plugins.DataPlugins.ShakeItV3.Settings;
    using SimHub.Plugins.Devices;
    public interface IShakeItChannelsInfoProvider
    {
        string DefaultSettingsKey { get; }
        bool IsConnected { get; }
        List<ChannelInformation> GetChannels(MotorsWithFrequencyOutputManagerBase manager);
        ChannelActivation CreateDefaultActivationFor(FFBPlacement placement, MotorsWithFrequencyOutputManagerBase manager);
        void LoadDefaultPlatformSettings(EffectsContainerBase container, ShakeItProfile profile);
        void UpdateOutput(Dictionary<int, ChannelValue> values);
        void Stop();
        FrequencyRange HardwareFrequencyRange();
        void SetSettings(ShakeItSettings settings);
        IEnumerable<DeviceSettingControl> GetSettingsControls();
    }
    public abstract class MotorsOutputManagerBase : IDeviceOutputManager
    {
        public ShakeItSettings Settings { get; set; }
        public IShakeItChannelsInfoProvider ShakeItChannelsInfoProvider { get; set; }
        public virtual Control GetEmbeddedSettingsControlFor(EffectsContainerBase container) { return null; }
        public virtual Control GetEmbeddedSettingsTitleControlFor(EffectsContainerBase container) { return null; }
        public virtual void LoadDefaultPlatformSettings(EffectsContainerBase container, ShakeItProfile profile) { }
        public virtual void Stop() { }
    }
    public abstract class MotorsWithFrequencyOutputManagerBase : MotorsOutputManagerBase
    {
        protected abstract void UpdateOutput(bool running, double globalGain, IList<EffectOutput> outputs, List<IAudioRenderer> renderers);
    }
    public class ShakeITV3PluginDevice<T, TSettings> : ShakeITV3PluginBase<T, TSettings>
        where T : IDeviceOutputManager, IOutputManager, new() where TSettings : ShakeItDeviceSettings<T>, new()
    {
        public ShakeITV3PluginDevice(string prefix, string title, bool fromDevice = false) : base(prefix, title, fromDevice) { }
    }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Device.MotorsWithFrequency
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Device;
    using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
    using SimHub.Plugins.DataPlugins.ShakeItV3.Settings;
    public class ChannelValue { public double Gain; public double Frequency; }
    public class ChannelInformation { public string Name { get; set; } }
    public class ChannelActivation { public bool IsEnabled { get; set; } }
    public class PlacementChannelsActivation
    { public Dictionary<int, ChannelActivation> Channels { get; } = new Dictionary<int, ChannelActivation>(); }
    public class DeviceChannelActivationSettings : AbstractSettingsBase
    { public Dictionary<FFBPlacement, PlacementChannelsActivation> Channels { get; } = new Dictionary<FFBPlacement, PlacementChannelsActivation>(); }
    public class MotorsWithFrequencyOutputManagerEffectsChannelsModel
    { public MotorsWithFrequencyOutputManagerEffectsChannelsModel(EffectsContainerBase container, MotorsWithFrequencyOutputManagerBase manager) { } }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.Device.MotorsWithFrequency.UI
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Device.MotorsWithFrequency;
    using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
    public class MotorsEffectsToChannelsUI : UserControl
    {
        public MotorsWithFrequencyOutputManagerEffectsChannelsModel Model { get; set; }
        public EffectsContainerBase EffectsContainerBase { get; set; }
    }
    public class MotorsEffectsToChannelsTitleUI : UserControl
    { public MotorsWithFrequencyOutputManagerEffectsChannelsModel Model { get; set; } }
}

namespace SimHub.Plugins.DataPlugins.ShakeItV3.UI
{
    using SimHub.Plugins.DataPlugins.ShakeItV3.Settings;
    public class EffectsListMain : UserControl
    { public ShakeItSettings Settings { get; set; } }
}
