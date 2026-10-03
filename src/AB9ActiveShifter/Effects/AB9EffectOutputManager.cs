using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using AB9ActiveShifter.Core;
using GameReaderCommon.Enums;
using SimHub.Plugins.DataPlugins.ShakeItV3.Device;
using SimHub.Plugins.DataPlugins.ShakeItV3.Device.MotorsWithFrequency;
using SimHub.Plugins.DataPlugins.ShakeItV3.Device.MotorsWithFrequency.UI;
using SimHub.Plugins.DataPlugins.ShakeItV3.Effects;
using SimHub.Plugins.DataPlugins.ShakeItV3.EffectsContainers;
using SimHub.Plugins.DataPlugins.ShakeItV3.Outputs.Audio.Renderers;
using SimHub.Plugins.DataPlugins.ShakeItV3.Settings;
using SimHub.Plugins.Devices;
using NativeOutput = SimHub.Plugins.DataPlugins.ShakeItV3.Outputs.EffectOutput;

namespace AB9ActiveShifter.Effects
{
    /// <summary>Public native output API only. No reflected internal mixer or hardware transport.</summary>
    public sealed class AB9EffectOutputManager : MotorsWithFrequencyOutputManagerBase
    {
        private sealed class Identity { public int Id; }
        private readonly ConditionalWeakTable<EffectBase, Identity> _ids = new ConditionalWeakTable<EffectBase, Identity>();
        private readonly ConditionalWeakTable<EffectsContainerBase, MotorsWithFrequencyOutputManagerEffectsChannelsModel> _models =
            new ConditionalWeakTable<EffectsContainerBase, MotorsWithFrequencyOutputManagerEffectsChannelsModel>();
        private int _nextId;

        public AB9EffectOutputManager() { ShakeItChannelsInfoProvider = new LeverChannels(); }

        protected override void UpdateOutput(bool gameinrace, double globalGain,
            IList<NativeOutput> effectOutputs, List<IAudioRenderer> renderers)
        {
            var settings = Settings as NativeEffectsSettings;
            if (settings == null || settings.Service == null) return;
            var tones = new List<NativeEffectTone>();
            DateTime now = DateTime.UtcNow;
            foreach (IAudioRenderer renderer in renderers)
            {
                var tone = renderer as IToneRenderer;
                if (tone == null) continue;
                // ShakeIt consumes TestMode before handing us the renderer. TestStart owns
                // the running test, including when there is no active game.
                bool test = NativeEffectMixer.IsTestActive(renderer.Effect.TestMode,
                    renderer.Effect.TestStart, renderer.Container.GetTestDuration(), now);
                if (!gameinrace && !test) continue;
                if (!Mapped(renderer.Container, renderer.Effect.Placement)) continue;
                renderer.Update(now);
                double level = globalGain / 100 * tone.GetCurrentGain() * renderer.Container.GetEffectiveGain() / 100;
                if (level <= 0) continue;
                Identity identity = _ids.GetValue(renderer.Effect, delegate { return new Identity { Id = ++_nextId }; });
                tones.Add(new NativeEffectTone
                {
                    Id = identity.Id,
                    Frequency = tone.GetCurrentFrequency(),
                    Level = level,
                    DelayMs = tone.GetCurrentDelay(),
                    Priority = renderer.IsPreemptive,
                    Test = test,
                    Grind = renderer.Container is AB9GrindEffectContainer
                });
            }
            settings.Service.Publish(new NativeEffectFrame(settings.Service.Epoch, Environment.TickCount, tones.ToArray()));
        }

        private static bool Mapped(EffectsContainerBase container, FFBPlacement placement)
        {
            DeviceChannelActivationSettings map = container.SettingsStore.GetSettings<DeviceChannelActivationSettings>();
            PlacementChannelsActivation channels;
            ChannelActivation channel;
            return !map.Channels.TryGetValue(placement, out channels)
                || !channels.Channels.TryGetValue(0, out channel) || channel.IsEnabled;
        }

        private MotorsWithFrequencyOutputManagerEffectsChannelsModel Model(EffectsContainerBase container)
        {
            return _models.GetValue(container, c => new MotorsWithFrequencyOutputManagerEffectsChannelsModel(c, this));
        }

        public override Control GetEmbeddedSettingsControlFor(EffectsContainerBase container)
        {
            return container.IsGroup ? null : new MotorsEffectsToChannelsUI { Model = Model(container), EffectsContainerBase = container };
        }

        public override Control GetEmbeddedSettingsTitleControlFor(EffectsContainerBase container)
        {
            return container.IsGroup ? null : new MotorsEffectsToChannelsTitleUI { Model = Model(container) };
        }

        public override void LoadDefaultPlatformSettings(EffectsContainerBase container, ShakeItProfile profile) { }

        public override void Stop()
        {
            var settings = Settings as NativeEffectsSettings;
            if (settings != null && settings.Service != null) settings.Service.Publish(NativeEffectFrame.Silent);
        }
    }

    public sealed class LeverChannels : IShakeItChannelsInfoProvider
    {
        private readonly List<ChannelInformation> _channels = new List<ChannelInformation> { new ChannelInformation { Name = "Lever" } };
        public string DefaultSettingsKey { get { return "AB9ActiveShifterLever"; } }
        public bool IsConnected { get { return true; } }
        public List<ChannelInformation> GetChannels(MotorsWithFrequencyOutputManagerBase manager) { return _channels; }
        public ChannelActivation CreateDefaultActivationFor(FFBPlacement placement, MotorsWithFrequencyOutputManagerBase manager)
        { return new ChannelActivation { IsEnabled = true }; }
        public FrequencyRange HardwareFrequencyRange()
        { return new FrequencyRange(NativeEffectMixer.MinimumFrequency, NativeEffectMixer.MaximumFrequency); }
        public void LoadDefaultPlatformSettings(EffectsContainerBase container, ShakeItProfile profile) { }
        public void UpdateOutput(Dictionary<int, ChannelValue> values) { }
        public void Stop() { }
        public void SetSettings(ShakeItSettings settings) { }
        public IEnumerable<DeviceSettingControl> GetSettingsControls() { yield break; }
    }
}
