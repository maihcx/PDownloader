namespace PDownloader.Utils
{
    public static class NavigationHandle
    {
        public static INavigationService? NavigationService;

        public static ObservableCollection<object> GetNavCardsInNamespace(string @namespace)
        {
            ObservableCollection<object> observableCollection = new ObservableCollection<object>();

            var pages = Assembly.GetExecutingAssembly()
                        .GetTypes()
                        .Where(t => t.IsClass &&
                                    t.IsSubclassOf(typeof(Page)) &&
                                    t.Namespace == @namespace)
                        .OrderBy(t => t.GetCustomAttribute<PageMetaAttribute>()?.SortIndex);

            foreach (var pageType in pages)
            {
                var attr = pageType.GetCustomAttribute<PageMetaAttribute>();
                if (attr != null)
                {
                    var NavViewItem = new NavigationViewItem
                    {
                        Content = attr?.DisplayName ?? pageType.Name.Replace("Page", ""),
                        Icon = new SymbolIcon { Symbol = attr?.Icon ?? SymbolRegular.Document24 },
                        TargetPageType = pageType
                    };
                    NavViewItem.SetBinding(NavigationViewItem.ContentProperty, new LocalizationExtension(attr?.DisplayNameKey ?? string.Empty));
                    var childPage = GetNavCardsInNamespace($"{pageType.FullName}s");
                    if (childPage.Count > 0)
                    {
                        foreach (var page in childPage)
                        {
                            NavViewItem.MenuItems.Add(page);
                        }
                    }

                    observableCollection.Add(NavViewItem);
                }
            }

            return observableCollection;
        }

        public static List<(Type PageType, Type ViewModelType)> GetPageViewModelPairs(string pageNamespace, string viewModelNamespace)
        {
            var assembly = Assembly.GetExecutingAssembly();

            var result = new List<(Type, Type)>();

            var pageTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Page)) && t.Namespace == pageNamespace);

            foreach (var pageType in pageTypes)
            {
                string viewModelName = pageType.Name.Replace("Page", "ViewModel");
                var viewModelType = assembly.GetType($"{viewModelNamespace}.{viewModelName}");

                if (viewModelType != null)
                {
                    result.Add((pageType, viewModelType));
                }
            }

            return result;
        }

        public static void SetupPageViewModelPairs(IServiceCollection service, string pageNamespace, string viewModelNamespace)
        {
            var assembly = Assembly.GetExecutingAssembly();

            var pageTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Page)) && t.Namespace == pageNamespace)
                .OrderBy(t => t.GetCustomAttribute<PageMetaAttribute>()?.SortIndex);

            foreach (var pageType in pageTypes)
            {
                string viewModelName = pageType.Name.Replace("Page", "ViewModel");
                var viewModelType = assembly.GetType($"{viewModelNamespace}.{viewModelName}");

                if (viewModelType != null)
                {
                    service.AddSingleton(pageType);
                    service.AddSingleton(viewModelType);
                }
            }
        }

        public static void SetupNavigationCard(ICollection<NavigationCard> navigationCards, string @namespace)
        {
            var pages = Assembly.GetExecutingAssembly()
                        .GetTypes()
                        .Where(t => t.IsClass &&
                                    t.IsSubclassOf(typeof(Page)) &&
                                    t.Namespace == @namespace)
                        .OrderBy(t => t.GetCustomAttribute<PageMetaAttribute>()?.SortIndex);

            foreach (var pageType in pages)
            {
                var attr = pageType.GetCustomAttribute<PageMetaAttribute>();
                if (attr != null)
                {
                    navigationCards.Append(new NavigationCard
                    {
                        NameKey = attr?.DisplayNameKey ?? pageType.Name.Replace("Page", ""),
                        Icon = attr?.Icon ?? SymbolRegular.Document24,
                        DescriptionKey = attr?.DescriptionKey ?? "",
                        PageType = pageType
                    });
                }
            }
        }

        public static ICollection<NavigationCard> GetNavigationCards(string[] @namespace, Type? excludePageType = null)
        {
            return new ObservableCollection<NavigationCard>(
                Assembly.GetExecutingAssembly()
                        .GetTypes()
                        .Where(t => t.IsClass &&
                                    t.IsSubclassOf(typeof(Page)) &&
                                    @namespace.Contains(t.Namespace) &&
                                    (excludePageType == null || t != excludePageType))
                        .OrderBy(t => t.GetCustomAttribute<PageMetaAttribute>()?.SortIndex)
                        .Select(pageType =>
                        {
                            var attr = pageType.GetCustomAttribute<PageMetaAttribute>();
                            return new NavigationCard()
                            {
                                NameKey = attr?.DisplayNameKey ?? pageType.Name.Replace("Page", ""),
                                Icon = attr?.Icon ?? SymbolRegular.Document24,
                                DescriptionKey = attr?.DescriptionKey ?? "",
                                PageType = pageType
                            };
                        })
            );
        }

    }
}
