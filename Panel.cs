// AdvancedPanel — managed wrapper.
// Copyright (c) 2026 Sotos. All rights reserved. See LICENSE.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AdvancedPanel
{
    /// <summary>Colour scheme the host UI should draw the panel with.</summary>
    public enum PanelTheme
    {
        /// <summary>Dark background, light text.</summary>
        Dark = 0,
        /// <summary>Light background, dark text.</summary>
        Light = 1
    }

    /// <summary>The kind of control a widget renders as.</summary>
    public enum WidgetKind
    {
        /// <summary>On/off switch. Value is 0 or 1.</summary>
        Toggle = 0,
        /// <summary>Continuous range between <c>Min</c> and <c>Max</c>.</summary>
        Slider = 1,
        /// <summary>Momentary push button. Fires a <see cref="EventKind.Click"/>.</summary>
        Button = 2,
        /// <summary>Non-interactive caption text.</summary>
        Label = 3
    }

    /// <summary>Why the native side called back into managed code.</summary>
    public enum EventKind
    {
        /// <summary>A button was pressed.</summary>
        Click = 1,
        /// <summary>A toggle flipped or a slider moved.</summary>
        ValueChanged = 2
    }

    /// <summary>Immutable snapshot of one widget's state, taken from the native core.</summary>
    public sealed class PanelWidget
    {
        /// <summary>Id of the tab that owns this widget.</summary>
        public string TabId { get; init; } = "";
        /// <summary>Widget id, unique within the panel.</summary>
        public string Id { get; init; } = "";
        /// <summary>What kind of control this is.</summary>
        public WidgetKind Kind { get; init; }
        /// <summary>Display text: label, caption or button title.</summary>
        public string Label { get; init; } = "";
        /// <summary>Lowest permitted value (sliders only).</summary>
        public double Min { get; init; }
        /// <summary>Highest permitted value (sliders only).</summary>
        public double Max { get; init; }
        /// <summary>Current value. 0 or 1 for toggles, 0 for buttons and labels.</summary>
        public double Value { get; init; }
        /// <summary>False when the host has greyed the widget out.</summary>
        public bool Enabled { get; init; }
    }

    /// <summary>Carries one widget interaction from native code to managed code.</summary>
    public sealed class PanelEventArgs : EventArgs
    {
        /// <summary>Tab that owns the widget.</summary>
        public string TabId { get; }
        /// <summary>Widget that was interacted with.</summary>
        public string WidgetId { get; }
        /// <summary>What happened.</summary>
        public EventKind Kind { get; }
        /// <summary>New value from the native core.</summary>
        public double Value { get; }

        public PanelEventArgs(string tabId, string widgetId, EventKind kind, double value)
        {
            TabId = tabId;
            WidgetId = widgetId;
            Kind = kind;
            Value = value;
        }
    }

    /// <summary>
    /// Managed handle over the AdvancedPanel native dylib.
    /// Safe to use from multiple threads; the native side is mutex-guarded.
    /// </summary>
    public sealed class Panel : IDisposable
    {
        private IntPtr _ctx;
        private NativeMethods.EventCallback? _callback; // kept alive for native code
        private bool _disposed;

        /// <summary>Panel title set at construction.</summary>
        public string Title { get; }
        /// <summary>Version string set at construction.</summary>
        public string Version { get; }
        /// <summary>Author string set at construction.</summary>
        public string Author { get; }
        /// <summary>Raised on the native thread when a widget reports interaction.</summary>
        public event EventHandler<PanelEventArgs>? WidgetChanged;

        /// <summary>Version string reported by the loaded native library.</summary>
        public static string NativeVersion =>
            Marshal.PtrToStringAnsi(NativeMethods.ap_version()) ?? "";
        /// <summary>ABI version reported by the loaded native library.</summary>
        public static int ApiVersion => NativeMethods.ap_api_version();

        public Panel(string title, string version, string author,
                             int width = 1280, int height = 720,
                             PanelTheme theme = PanelTheme.Dark)
        {
            Title = title;
            Version = version;
            Author = author;

            var config = new NativeMethods.NativeConfig
            {
                Title = Marshal.StringToHGlobalAnsi(title),
                Version = Marshal.StringToHGlobalAnsi(version),
                Author = Marshal.StringToHGlobalAnsi(author),
                Width = width,
                Height = height,
                Theme = (int)theme,
                Flags = 0
            };

            try
            {
                _ctx = NativeMethods.ap_create(ref config);
            }
            finally
            {
                Marshal.FreeHGlobal(config.Title);
                Marshal.FreeHGlobal(config.Version);
                Marshal.FreeHGlobal(config.Author);
            }

            if (_ctx == IntPtr.Zero)
                throw new InvalidOperationException("ap_create returned a null context.");

            _callback = OnNativeEvent;
            NativeMethods.ap_set_callback(_ctx, _callback, IntPtr.Zero);
        }

        private void OnNativeEvent(IntPtr userData, IntPtr tabId, IntPtr widgetId,
                                   int kind, double value)
        {
            WidgetChanged?.Invoke(this, new PanelEventArgs(
                Marshal.PtrToStringAnsi(tabId) ?? "",
                Marshal.PtrToStringAnsi(widgetId) ?? "",
                (EventKind)kind,
                value));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Panel));
        }

        /// <summary>Number of tabs currently in the panel.</summary>
        public int TabCount { get { ThrowIfDisposed(); return NativeMethods.ap_tab_count(_ctx); } }

        /// <summary>Adds a tab. Returns false if <paramref name="tabId"/> already exists.</summary>
        public bool AddTab(string tabId, string title)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_add_tab(_ctx, tabId, title) == 0;
        }

        /// <summary>Removes a tab and every widget inside it.</summary>
        public bool RemoveTab(string tabId)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_remove_tab(_ctx, tabId) == 0;
        }

        /// <summary>Adds an on/off switch to a tab.</summary>
        public bool AddToggle(string tabId, string widgetId, string label, bool initial = false)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_add_toggle(_ctx, tabId, widgetId, label, initial ? 1 : 0) == 0;
        }

        /// <summary>Adds a slider bounded by <paramref name="min"/> and <paramref name="max"/>.</summary>
        public bool AddSlider(string tabId, string widgetId, string label,
                              double min, double max, double initial)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_add_slider(_ctx, tabId, widgetId, label, min, max, initial) == 0;
        }

        /// <summary>Adds a momentary push button.</summary>
        public bool AddButton(string tabId, string widgetId, string label)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_add_button(_ctx, tabId, widgetId, label) == 0;
        }

        /// <summary>Adds a non-interactive caption.</summary>
        public bool AddLabel(string tabId, string widgetId, string text)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_add_label(_ctx, tabId, widgetId, text) == 0;
        }

        /// <summary>Reads a toggle. Returns false for unknown ids.</summary>
        public bool GetToggle(string widgetId)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_get_toggle(_ctx, widgetId) == 1;
        }

        /// <summary>Sets a toggle from code. Does not raise <see cref="WidgetChanged"/>.</summary>
        public bool SetToggle(string widgetId, bool value)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_set_toggle(_ctx, widgetId, value ? 1 : 0) == 0;
        }

        /// <summary>Reads a slider value. Returns 0 for unknown ids.</summary>
        public double GetSlider(string widgetId)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_get_slider(_ctx, widgetId);
        }

        /// <summary>Sets a slider from code; the value is clamped to its range.</summary>
        public bool SetSlider(string widgetId, double value)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_set_slider(_ctx, widgetId, value) == 0;
        }

        /// <summary>Simulates a click, dispatching a <see cref="EventKind.Click"/> event.</summary>
        public bool Click(string tabId, string widgetId)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_dispatch_event(_ctx, tabId, widgetId, (int)EventKind.Click, 0.0) == 0;
        }

        /// <summary>Reports a slider drag from the host UI as a <see cref="EventKind.ValueChanged"/> event.</summary>
        public bool SetSliderFromUI(string tabId, string widgetId, double value)
        {
            ThrowIfDisposed();
            return NativeMethods.ap_dispatch_event(_ctx, tabId, widgetId,
                (int)EventKind.ValueChanged, value) == 0;
        }

        /// <summary>Snapshot of every widget in the panel, in insertion order.</summary>
        public IReadOnlyList<PanelWidget> Widgets
        {
            get
            {
                ThrowIfDisposed();
                var list = new List<PanelWidget>();
                int count = NativeMethods.ap_widget_count(_ctx);
                for (int i = 0; i < count; i++)
                {
                    if (NativeMethods.ap_widget_at(_ctx, i, out var info) != 0) continue;
                    list.Add(new PanelWidget
                    {
                        TabId = Marshal.PtrToStringAnsi(info.TabId) ?? "",
                        Id = Marshal.PtrToStringAnsi(info.Id) ?? "",
                        Kind = (WidgetKind)info.Kind,
                        Label = Marshal.PtrToStringAnsi(info.Label) ?? "",
                        Min = info.MinValue,
                        Max = info.MaxValue,
                        Value = info.Value,
                        Enabled = info.Enabled != 0
                    });
                }
                return list;
            }
        }

        /// <summary>Serialises the whole panel to JSON for saving.</summary>
        public string ToJson()
        {
            ThrowIfDisposed();
            IntPtr ptr = NativeMethods.ap_serialize(_ctx);
            if (ptr == IntPtr.Zero) return "{}";
            try { return Marshal.PtrToStringAnsi(ptr) ?? "{}"; }
            finally { NativeMethods.ap_free_string(ptr); }
        }

        /// <summary>Destroys the native context. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ctx != IntPtr.Zero)
            {
                NativeMethods.ap_destroy(_ctx);
                _ctx = IntPtr.Zero;
            }
            _callback = null;
        }

        ~Panel() { Dispose(); }
    }
}
