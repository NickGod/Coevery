﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using System.Web.Routing;
using Orchard.ContentManagement;
using Orchard.Logging;
using Orchard.Security;
using Orchard.Security.Permissions;

namespace Orchard.UI.Navigation {
    public class NavigationManager : INavigationManager {
        private readonly IEnumerable<INavigationProvider> _navigationProviders;
        private readonly IEnumerable<IMenuProvider> _menuProviders;
        private readonly IAuthorizationService _authorizationService;
        private readonly UrlHelper _urlHelper;
        private readonly IOrchardServices _orchardServices;

        public NavigationManager(
            IEnumerable<INavigationProvider> navigationProviders, 
            IEnumerable<IMenuProvider> menuProviders,
            IAuthorizationService authorizationService, 
            UrlHelper urlHelper, 
            IOrchardServices orchardServices) {
            _navigationProviders = navigationProviders;
            _menuProviders = menuProviders;
            _authorizationService = authorizationService;
            _urlHelper = urlHelper;
            _orchardServices = orchardServices;
            Logger = NullLogger.Instance;
        }

        public ILogger Logger { get; set; }

        public IEnumerable<MenuItem> BuildMenu(string menuName) {
            var sources = GetSources(menuName);
            return FinishMenu(Reduce(Merge(sources)).ToArray());
        }

        public IEnumerable<MenuItem> BuildMenu(IContent menu) {
            var sources = GetSources(menu);
            return FinishMenu(Reduce(Arrange(Merge(sources))).ToArray());
        }

        public IEnumerable<string> BuildImageSets(string menuName) {
            return GetImageSets(menuName).SelectMany(imageSets => imageSets.Distinct()).Distinct();
        }

        private IEnumerable<MenuItem> FinishMenu(IEnumerable<MenuItem> menuItems) {
            foreach (var menuItem in menuItems) {
                menuItem.Href = GetUrl(menuItem.Url, menuItem.RouteValues);
                menuItem.Items = FinishMenu(menuItem.Items.ToArray());
            }

            return menuItems;
        }

        public string GetUrl(string menuItemUrl, RouteValueDictionary routeValueDictionary) {
            var url = string.IsNullOrEmpty(menuItemUrl) && (routeValueDictionary == null || routeValueDictionary.Count == 0)
                          ? "~/"
                          : !string.IsNullOrEmpty(menuItemUrl)
                                ? menuItemUrl
                                : _urlHelper.RouteUrl(routeValueDictionary);

            if (!string.IsNullOrEmpty(url) && _urlHelper.RequestContext.HttpContext != null &&
                !(url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("/"))) {
                if (url.StartsWith("~/")) {
                    url = url.Substring(2);
                }
                var appPath = _urlHelper.RequestContext.HttpContext.Request.ApplicationPath;
                if (appPath == "/")
                    appPath = "";
                url = string.Format("{0}/{1}", appPath, url);
            }
            return url;
        }

        /// <summary>
        /// Updates the items by checking for permissions
        /// </summary>
        private IEnumerable<MenuItem> Reduce(IEnumerable<MenuItem> items) {
            var hasDebugShowAllMenuItems = _authorizationService.TryCheckAccess(Permission.Named("DebugShowAllMenuItems"), _orchardServices.WorkContext.CurrentUser, null);
            foreach (var item in items) {
                if (hasDebugShowAllMenuItems ||
                    !item.Permissions.Any() ||
                    item.Permissions.Any(x => _authorizationService.TryCheckAccess(x, _orchardServices.WorkContext.CurrentUser, null))) {
                    yield return new MenuItem {
                        Items = Reduce(item.Items),
                        Permissions = item.Permissions,
                        Position = item.Position,
                        RouteValues = item.RouteValues,
                        LocalNav = item.LocalNav,
                        Text = item.Text,
                        IdHint = item.IdHint,
                        Classes = item.Classes,
                        Url = item.Url,
                        LinkToFirstChild = item.LinkToFirstChild,
                        Href = item.Href,
                        MenuId = item.MenuId
                    };
                }
            }
        }

        private IEnumerable<IEnumerable<MenuItem>> GetSources(string menuName) {
            foreach (var provider in _navigationProviders) {
                if (provider.MenuName == menuName) {
                    var builder = new NavigationBuilder();
                    IEnumerable<MenuItem> items = null;
                    try {
                        provider.GetNavigation(builder);
                        items = builder.Build();
                    }
                    catch (Exception ex) {
                        Logger.Error(ex, "Unexpected error while querying a navigation provider. It was ignored. The menu provided by the provider may not be complete.");
                    }
                    if (items != null) {
                        yield return items;
                    }
                }
            }
        }

        private IEnumerable<IEnumerable<MenuItem>> GetSources(IContent menu) {
            foreach (var provider in _menuProviders) {
                var builder = new NavigationBuilder();
                IEnumerable<MenuItem> items = null;
                try {
                    provider.GetMenu(menu, builder);
                    items = builder.Build();
                }
                catch (Exception ex) {
                    Logger.Error(ex, "Unexpected error while querying a menu provider. It was ignored. The menu provided by the provider may not be complete.");
                }
                if (items != null) {
                    yield return items;
                }
            }
        }

        private IEnumerable<IEnumerable<string>> GetImageSets(string menuName) {
            foreach (var provider in _navigationProviders) {
                if (provider.MenuName == menuName) {
                    var builder = new NavigationBuilder();
                    IEnumerable<string> imageSets = null;
                    try {
                        provider.GetNavigation(builder);
                        imageSets = builder.BuildImageSets();
                    }
                    catch (Exception ex) {
                        Logger.Error(ex, "Unexpected error while querying a navigation provider. It was ignored. The menu provided by the provider may not be complete.");
                    }
                    if (imageSets != null) {
                        yield return imageSets;
                    }
                }
            }
        }

        private static IEnumerable<MenuItem> Merge(IEnumerable<IEnumerable<MenuItem>> sources) {
            var comparer = new MenuItemComparer();
            var orderer = new FlatPositionComparer();

            return sources.SelectMany(x => x).ToArray()
                // group same menus
                .GroupBy(key => key, (key, items) => Join(items), comparer)
                // group same position
                .GroupBy(item => item.Position)
                // order position groups by position
                .OrderBy(positionGroup => positionGroup.Key, orderer)
                // ordered by item text in the postion group
                .SelectMany(positionGroup => positionGroup.OrderBy(item => item.Text == null ? "" : item.Text.TextHint));
        }

        /// <summary>
        /// Organizes a list of <see cref="MenuItem"/> into a hierarchy based on their positions
        /// </summary>
        private static IEnumerable<MenuItem> Arrange(IEnumerable<MenuItem> items) {
            
            var result = new List<MenuItem>();
            var index = new Dictionary<string, MenuItem>();

            foreach (var item in items) {
                MenuItem parent = null;
                var position = item.Position;
                

                while (parent == null && !String.IsNullOrEmpty(position)) {
                    if (index.TryGetValue(position, out parent)) {
                        parent.Items = parent.Items.Concat(new [] { item });
                    }

                    position = position.Substring(0, position.Length - 1);
                };

                if (!index.ContainsKey(item.Position)) {
                    // prevent invalid positions
                    index.Add(item.Position, item);    
                }
                

                // if the current element has no parent, it's a top level item
                if (parent == null) {
                    result.Add(item);
                }
            }

            return result;
        }

        static MenuItem Join(IEnumerable<MenuItem> items) {
            if (items.Count() < 2)
                return items.Single();

            var joined = new MenuItem {
                Text = items.First().Text,
                IdHint = items.Select(x => x.IdHint).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                Classes = items.Select(x => x.Classes).FirstOrDefault(x => x != null && x.Count > 0),
                Url = items.Select(x => x.Url).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                Href = items.Select(x => x.Href).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                LinkToFirstChild = items.First().LinkToFirstChild,
                RouteValues = items.Select(x => x.RouteValues).FirstOrDefault(x => x != null),
                LocalNav = items.Any(x => x.LocalNav),
                Items = Merge(items.Select(x => x.Items)).ToArray(),
                Position = SelectBestPositionValue(items.Select(x => x.Position)),
                Permissions = items.SelectMany(x => x.Permissions).Distinct(),
                MenuId = items.First().MenuId,
            };
            return joined;
        }

        private static string SelectBestPositionValue(IEnumerable<string> positions) {
            var comparer = new FlatPositionComparer();
            return positions.Aggregate(string.Empty,
                                       (agg, pos) =>
                                       string.IsNullOrEmpty(agg)
                                           ? pos
                                           : string.IsNullOrEmpty(pos)
                                                 ? agg
                                                 : comparer.Compare(agg, pos) < 0 ? agg : pos);
        }
    }
}