using System.Windows.Media;

namespace VisionIPC.Deployment.App.ViewModels
{
    public enum NavigationKind
    {
        Overview,
        Group,
        Completion
    }

    public sealed class NavigationItemViewModel : ObservableObject
    {
        private string name;
        private string statusGlyph;
        private Brush statusBrush;
        private string progressText;

        public NavigationItemViewModel(NavigationKind kind, string name)
        {
            Kind = kind;
            this.name = name;
            statusGlyph = "○";
            statusBrush = Brushes.Gray;
            progressText = string.Empty;
        }

        public NavigationKind Kind { get; private set; }
        public GroupViewModel Group { get; set; }

        public string Name
        {
            get { return name; }
            set { SetProperty(ref name, value); }
        }

        public string StatusGlyph
        {
            get { return statusGlyph; }
            set { SetProperty(ref statusGlyph, value); }
        }

        public Brush StatusBrush
        {
            get { return statusBrush; }
            set { SetProperty(ref statusBrush, value); }
        }

        public string ProgressText
        {
            get { return progressText; }
            set { SetProperty(ref progressText, value); }
        }
    }
}
