using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Monitor_Pc.Models
{
    public partial class SensorCategory : ObservableObject
    {
        [ObservableProperty] private string name        = string.Empty;
        [ObservableProperty] private string accentColor = "#FFFFFF";
        [ObservableProperty] private int    orderIndex;

        public ObservableCollection<HardwareSensor> Sensors { get; } = new();
    }
}
