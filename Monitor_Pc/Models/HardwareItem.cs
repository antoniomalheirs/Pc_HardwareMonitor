using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Monitor_Pc.Models
{
    public partial class HardwareItem : ObservableObject
    {
        [ObservableProperty] private string name         = string.Empty;
        [ObservableProperty] private string hardwareType = string.Empty;
        [ObservableProperty] private string category     = string.Empty;
        [ObservableProperty] private string icon         = "";
        [ObservableProperty] private string accentColor  = "#FFFFFF";
        [ObservableProperty] private bool   isCpu;
        [ObservableProperty] private bool   isGpu;

        public ObservableCollection<SensorCategory> Categories { get; } = new();

        partial void OnHardwareTypeChanged(string value)
        {
            IsCpu = value == "Cpu";
            IsGpu = value.StartsWith("Gpu");
        }
    }
}
