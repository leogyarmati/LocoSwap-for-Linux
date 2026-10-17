using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace LocoSwap
{
    public class DirectoryItem : ModelBase
    {
        private string _name;
        private string _path;
        private bool _populated;
        private bool _isSelected;
        private bool _isExpanded;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        public string Path
        {
            get => _path;
            set => SetProperty(ref _path, value);
        }
        public bool Populated
        {
            get => _populated;
            set => SetProperty(ref _populated, value);
        }
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
        /// <summary>Expanding a node lazily loads its children.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value) PopulateSubDirectories();
            }
        }
        public ObservableCollection<DirectoryItem> SubDirectories { get; set; }

        public DirectoryItem()
        {
            this.Populated = false;
            this.SubDirectories = new ObservableCollection<DirectoryItem>();
        }

        public void PopulateSubDirectories()
        {
            if (this.Populated || this.Path == "dummy") return;
            SubDirectories.Clear();

            DirectoryInfo[] directories;
            try
            {
                directories = new DirectoryInfo(Path).GetDirectories()
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception)
            {
                directories = new DirectoryInfo[0];
            }

            foreach (var directory in directories)
            {
                var item = new DirectoryItem
                {
                    Name = directory.Name,
                    Path = directory.FullName
                };
                item.SubDirectories.Add(new DirectoryItem
                {
                    Name = "Loading...",
                    Path = "dummy"
                });

                SubDirectories.Add(item);
            }
            Populated = true;
        }
    }
}
