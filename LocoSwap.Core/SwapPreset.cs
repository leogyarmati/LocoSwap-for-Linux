using System.Collections.ObjectModel;
using System.Linq;

namespace LocoSwap
{
    public class SwapPresetItem : ModelBase
    {
        public string TargetName { get; set; }
        public string TargetXmlPath { get; set; }
        public string NewName { get; set; }
        public string NewXmlPath { get; set; }
        public float NewLength { get; set; }
    }

    public class SwapPreset
    {
        public ObservableCollection<SwapPresetItem> List { get; set; } = new ObservableCollection<SwapPresetItem>();

        public bool Contains(string targetXmlPath)
        {
            return List.Any((item) => item.TargetXmlPath.Equals(targetXmlPath, System.StringComparison.OrdinalIgnoreCase));
        }

        public SwapPresetItem Find(string targetXmlPath)
        {
            return List.Where((item) => item.TargetXmlPath.Equals(targetXmlPath, System.StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        }
    }
}
