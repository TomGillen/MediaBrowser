﻿(function ($, document) {

    function reloadList(page) {

        Dashboard.showLoadingMsg();

        var promise1 = ApiClient.getAvailablePlugins({
            TargetSystems: 'Server'
        });

        var promise2 = ApiClient.getInstalledPlugins();

        $.when(promise1, promise2).done(function (response1, response2) {
            renderInstalled(page, response1[0], response2[0]);
            renderCatalog(page, response1[0], response2[0]);
        });
    }

    function getCategories() {

        var context = getParameterByName('context');

        var categories = [];

        if (context == 'sync') {
            categories.push('Sync');
        }
        else if (context == 'livetv') {
            categories.push('Live TV');
        }
        else if (context == 'notifications') {
            categories.push('Notifications');
        }

        return categories;
    }

    function renderInstalled(page, availablePlugins, installedPlugins) {

        requirejs(['scripts/pluginspage'], function() {
            var category = getCategories()[0];

            installedPlugins = installedPlugins.filter(function (i) {

                var catalogEntry = availablePlugins.filter(function (a) {
                    return a.guid == i.Id;
                })[0];

                return catalogEntry && catalogEntry.category == category;

            });

            PluginsPage.renderPlugins(page, installedPlugins);
        });
    }

    function renderCatalog(page, availablePlugins, installedPlugins) {

        requirejs(['scripts/plugincatalogpage'], function () {
            var categories = getCategories();

            PluginCatalog.renderCatalog({

                catalogElement: $('.catalog', page),
                availablePlugins: availablePlugins,
                installedPlugins: installedPlugins,
                categories: categories,
                showCategory: false,
                context: getParameterByName('context'),
                targetSystem: 'Server'
            });
        });
    }

    $(document).on('pagebeforeshow pageshow', "#appServicesPage", function () {

        // This needs both events for the helpurl to get done at the right time

        var page = this;

        var context = getParameterByName('context');

        $('.sectionTabs', page).hide();

        if (context == 'sync') {
            Dashboard.setPageTitle(Globalize.translate('TitleSync'));
            page.setAttribute('data-helpurl', 'https://github.com/MediaBrowser/Wiki/wiki/Sync');
        }
        else if (context == 'livetv') {
            Dashboard.setPageTitle(Globalize.translate('TitleLiveTV'));
            page.setAttribute('data-helpurl', 'https://github.com/MediaBrowser/Wiki/wiki/Live%20TV');
        }
        else if (context == 'notifications') {
            Dashboard.setPageTitle(Globalize.translate('TitleNotifications'));
            page.setAttribute('data-helpurl', 'https://github.com/MediaBrowser/Wiki/wiki/Notifications');
        }

        $('.sectionTabs', page).hide();
        $('.' + context + 'SectionTabs', page).show();

    }).on('pageshow', "#appServicesPage", function () {

        var page = this;

        reloadList(page);
    });

})(jQuery, document);