// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.Utils;

public static class NavigationHandle
{
    public static INavigationService? NavigationService;

    public static ObservableCollection<object> GetNavCardsInNamespace(string @namespace)
    {
        ObservableCollection<object> observableCollection = new ObservableCollection<object>();

        IOrderedEnumerable<Type> pages = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .Where(t => t.IsClass &&
                                t.IsSubclassOf(typeof(Page)) &&
                                t.Namespace == @namespace)
                    .OrderBy(t => t.GetCustomAttribute<PageMetaAttribute>()?.SortIndex);

        foreach (Type? pageType in pages)
        {
            PageMetaAttribute? attr = pageType.GetCustomAttribute<PageMetaAttribute>();
            if (attr != null)
            {
                var NavViewItem = new NavigationViewItem
                {
                    Content = attr?.DisplayName ?? pageType.Name.Replace("Page", ""),
                    Icon = new SymbolIcon { Symbol = attr?.Icon ?? SymbolRegular.Document24 },
                    TargetPageType = pageType
                };
                NavViewItem.SetBinding(NavigationViewItem.ContentProperty, new LocalizationExtension(attr?.DisplayNameKey ?? string.Empty));
                ObservableCollection<object> childPage = GetNavCardsInNamespace($"{pageType.FullName}s");
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

        IEnumerable<Type> pageTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Page)) && t.Namespace == pageNamespace);

        foreach (Type? pageType in pageTypes)
        {
            string viewModelName = pageType.Name.Replace("Page", "ViewModel");
            Type? viewModelType = assembly.GetType($"{viewModelNamespace}.{viewModelName}");

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

        IOrderedEnumerable<Type> pageTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Page)) && t.Namespace == pageNamespace)
            .OrderBy(t => t.GetCustomAttribute<PageMetaAttribute>()?.SortIndex);

        foreach (Type? pageType in pageTypes)
        {
            string viewModelName = pageType.Name.Replace("Page", "ViewModel");
            Type? viewModelType = assembly.GetType($"{viewModelNamespace}.{viewModelName}");

            if (viewModelType != null)
            {
                service.AddSingleton(pageType);
                service.AddSingleton(viewModelType);
            }
        }
    }

    public static void SetupNavigationCard(ICollection<NavigationCard> navigationCards, string @namespace)
    {
        IOrderedEnumerable<Type> pages = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .Where(t => t.IsClass &&
                                t.IsSubclassOf(typeof(Page)) &&
                                t.Namespace == @namespace)
                    .OrderBy(t => t.GetCustomAttribute<PageMetaAttribute>()?.SortIndex);

        foreach (Type? pageType in pages)
        {
            PageMetaAttribute? attr = pageType.GetCustomAttribute<PageMetaAttribute>();
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
                        PageMetaAttribute? attr = pageType.GetCustomAttribute<PageMetaAttribute>();
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
