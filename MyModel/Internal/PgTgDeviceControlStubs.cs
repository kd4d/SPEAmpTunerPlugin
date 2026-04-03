#nullable enable

using System.Collections.Generic;

// Only used when the installed PgTgBridge assemblies do not include PgTg.Web types.
// If you see duplicate-type errors with the real bridge, delete this file (see README).

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
