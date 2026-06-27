namespace PDownloader.Views.Pages
{
    [PageMeta("page_config_title", "page_config_summary", SymbolRegular.ArrowTrendingSettings24, 2, false)]
    public partial class ConfigPage : INavigableView<ConfigViewModel>
    {
        public ConfigViewModel ViewModel { get; }

        public ConfigPage(ConfigViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
