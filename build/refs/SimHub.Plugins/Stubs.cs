// Reference-only declarations of the SimHub plugin API this project uses. No logic lives
// here and none of it runs: SimHub's real SimHub.Plugins.dll is what loads at runtime.
// Signatures were taken by reflecting over the real assembly - see build/refs/README.md for
// why an inexact signature is worse than a missing one.

using System;
using System.Windows.Controls;
using System.Windows.Media;
using GameReaderCommon;

namespace SimHub.Plugins
{
    /// <summary>SimHub's plugin host. Only the property bridge is used by this plugin.</summary>
    public class PluginManager
    {
        public object GetPropertyValue(string name) { return null; }
    }

    /// <summary>Handle returned when an event is registered. Never inspected by this plugin.</summary>
    public class EventTrigger
    {
    }

    public interface IPlugin
    {
        PluginManager PluginManager { get; set; }
        void Init(PluginManager pluginManager);
        void End(PluginManager pluginManager);
    }

    public interface IDataPlugin
    {
        void DataUpdate(PluginManager pluginManager, ref GameData data);
    }

    public interface IWPFSettingsV2
    {
        string LeftMenuTitle { get; }
        ImageSource PictureIcon { get; }
        Control GetWPFSettingsControl(PluginManager pluginManager);
    }

    /// <summary>Marks a plugin whose teardown is separate from the per-game-change stop.</summary>
    public interface IReusable
    {
        void FinalizePlugin();
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PluginNameAttribute : Attribute
    {
        public PluginNameAttribute(string name) { this.name = name; }
        public string name;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PluginAuthorAttribute : Attribute
    {
        public PluginAuthorAttribute(string name) { this.name = name; }
        public string name;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PluginDescriptionAttribute : Attribute
    {
        public PluginDescriptionAttribute(string description) { this.description = description; }
        public string description;
    }

    /// <summary>
    /// The extension methods a plugin uses to publish properties, events and actions. The
    /// declaring type name matters: an extension call compiles to a static call on this exact
    /// type, so renaming it would produce IL that cannot bind to the real assembly.
    /// </summary>
    public static class IPluginExtensions
    {
        public static void AttachDelegate<T, U>(this T plugin, string name, Func<U> valueProvider)
            where T : IPlugin
        {
        }

        public static void AttachDelegate<T, U>(this T plugin, string name, Func<U> valueProvider, bool hidden)
            where T : IPlugin
        {
        }

        public static EventTrigger AddEvent<T>(this T plugin, string eventName)
            where T : IPlugin
        {
            return null;
        }

        public static void TriggerEvent<T>(this T plugin, string eventName)
            where T : IPlugin
        {
        }

        public static void AddAction<T>(
            this T plugin,
            string actionName,
            Action<PluginManager, string> actionStart,
            Action<PluginManager, string> actionEnd = null)
            where T : IPlugin
        {
        }

        public static T ReadCommonSettings<T>(this IPlugin plugin, string settingsName, Func<T> defaultValueFactory)
        {
            return default(T);
        }

        public static void SaveCommonSettings<T>(this IPlugin plugin, string settingsName, T settingsObject)
        {
        }

        public static void SaveCommonSettings<T>(this IPlugin plugin, string settingsName, T settingsObject, int maxVersions)
        {
        }
    }
}

namespace SimHub.Plugins.Styles
{
    using System.Windows;
    using System.Windows.Controls.Primitives;

    /// <summary>
    /// A titled block of settings. Properties are dependency properties because the XAML binds
    /// some of them, and WPF rejects a Binding on a plain CLR property at compile time.
    /// </summary>
    public class SHSection : ContentControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(SHSection));

        public static readonly DependencyProperty ShowSeparatorProperty =
            DependencyProperty.Register("ShowSeparator", typeof(bool), typeof(SHSection));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public bool ShowSeparator
        {
            get { return (bool)GetValue(ShowSeparatorProperty); }
            set { SetValue(ShowSeparatorProperty, value); }
        }
    }

    public class SHTabControl : TabControl
    {
    }

    public class SHTabItem : TabItem
    {
    }

    public class SHButtonBase : Button
    {
    }

    public class SHButtonPrimary : SHButtonBase
    {
    }

    public class SHButtonSecondary : SHButtonBase
    {
    }

    public class SHToggleCheckbox : CheckBox
    {
    }

    public class SHExpander : Expander
    {
    }
}

namespace SimHub.Plugins.UI
{
    using System.Windows;

    /// <summary>
    /// Slider with a caption and a reset arrow. Every property is a dependency property: the
    /// settings page binds Value, and sets the rest from markup.
    /// </summary>
    public class TitledSlider : Control
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(TitledSlider));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(TitledSlider));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(double), typeof(TitledSlider));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(double), typeof(TitledSlider));

        public static readonly DependencyProperty ResetValueProperty =
            DependencyProperty.Register("ResetValue", typeof(double), typeof(TitledSlider));

        public static readonly DependencyProperty TickFrequencyProperty =
            DependencyProperty.Register("TickFrequency", typeof(double), typeof(TitledSlider));

        public static readonly DependencyProperty ShowResetButtonProperty =
            DependencyProperty.Register("ShowResetButton", typeof(bool), typeof(TitledSlider));

        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register("IsChecked", typeof(bool), typeof(TitledSlider));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public double ResetValue
        {
            get { return (double)GetValue(ResetValueProperty); }
            set { SetValue(ResetValueProperty, value); }
        }

        public double TickFrequency
        {
            get { return (double)GetValue(TickFrequencyProperty); }
            set { SetValue(TickFrequencyProperty, value); }
        }

        public bool ShowResetButton
        {
            get { return (bool)GetValue(ShowResetButtonProperty); }
            set { SetValue(ShowResetButtonProperty, value); }
        }

        public bool IsChecked
        {
            get { return (bool)GetValue(IsCheckedProperty); }
            set { SetValue(IsCheckedProperty, value); }
        }
    }
}
