#nullable enable

using System.Collections.Generic;

// Stubs for IDE / CI builds when the installed PgTgBridge copy does not yet expose these types
// in referenced assemblies. Remove this file when your PgTgBridge SDK includes Device Control types
// (or if you get CS0433 duplicate type — then the bridge already provides them).

namespace PgTg.Web
{
    public sealed class DeviceControlDefinition
    {
        public List<DeviceControlElement>? Elements { get; set; }
        public FanControlDefinition? FanControl { get; set; }
    }

    public sealed class DeviceControlElement
    {
        public string ActiveColor { get; set; } = "gray";
        public string InactiveColor { get; set; } = "gray";
        public string ActiveText { get; set; } = "";
        public string InactiveText { get; set; } = "";
        public string? ActiveCommand { get; set; }
        public string? InactiveCommand { get; set; }
        public string ResponseKey { get; set; } = "";
        public string ActiveValue { get; set; } = "";
        public bool IsClickable { get; set; }
        public bool IsPowerIndicator { get; set; }
    }

    public sealed class FanControlDefinition
    {
        public string ResponseKey { get; set; } = "FN";
        public int MaxSpeed { get; set; } = 5;
        public string SetCommandPrefix { get; set; } = "$FC";
    }
}
